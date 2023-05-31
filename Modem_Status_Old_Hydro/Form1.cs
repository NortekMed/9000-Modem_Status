using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Renci.SshNet;
using FirebirdSql.Data.FirebirdClient;
using static System.Net.Mime.MediaTypeNames;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace Modem_Status_Old_Hydro
{
    public partial class Form1 : Form
    {
        static CultureInfo ci;

        public static List<SysHydro> Modems_List = new List<SysHydro>();
        public static List<System.Threading.Timer> Modems_Timers = new List<System.Threading.Timer>();

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            Console.SetOut(new ControlWriter(textBox_Console));

            //Load Config
            StreamReader conf = new StreamReader("config.txt");

            List<string> arguments = new List<string>();
            string line = "";
            try
            {
                // pour s'assurer d'avoir un '.' pour séparateur décimal
                Form1.ci = (CultureInfo)Thread.CurrentThread.CurrentCulture.Clone();
                Form1.ci.NumberFormat.NumberDecimalSeparator = ".";
                Thread.CurrentThread.CurrentCulture = ci;
                Thread.CurrentThread.CurrentUICulture = ci;


                Console.WriteLine("Read config file conf.txt");

                while ((line = conf.ReadLine()) != null)
                {
                    Console.WriteLine(line);
                    if ((line.Length != 0) && !line.Contains("//"))
                        arguments.Add(line);
                }

                conf.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Source + " : " + ex.Message);
            }

            string[] argTab = arguments.ToArray();

            SysHydro Current_Hydro = new SysHydro();

            for (int i = 0; i < argTab.Length; i++)
            {
                if (argTab[i].Contains("="))
                {
                    string arg = argTab[i].Split('=')[0];
                    string value = argTab[i].Split('=')[1];

                    switch (arg.ToUpper())
                    {
                        case "ADRESS":
                            if (Current_Hydro.Adress == "")
                            {
                                Current_Hydro.Adress = value;
                            }
                            else
                            {
                                Modems_List.Add(Current_Hydro);
                                Current_Hydro = new SysHydro();
                                Current_Hydro.Adress = value;
                            }
                            break;

                        case "PORT":
                            int.TryParse(value, out Current_Hydro.Port);
                            break;

                        case "PERIOD":
                            int.TryParse(value, out Current_Hydro.Period);
                            break;

                        case "DB_PATH":
                            Current_Hydro.DB_Path = value;
                            break;


                        default:
                            break;
                    }
                }
            }

            if (Current_Hydro.Adress != "")
            {
                Modems_List.Add(Current_Hydro);
            }

            Console.WriteLine("");
            Console.WriteLine("    --------------------------        ");
            Console.WriteLine("");

            // Lancement des Timers
            for (int i = 0; i< Modems_List.Count; i++)
            {
                Console.WriteLine("Start TIMER : " + Modems_List[i].Adress + ":" + Modems_List[i].Port.ToString());
                Modems_Timers.Add(new System.Threading.Timer(new System.Threading.TimerCallback(Modems_List[i].checkModem), this, i*15000, Modems_List[i].Period *60000));
                //Modems_Timers.Add(new System.Threading.Timer(new System.Threading.TimerCallback(Modems_List[i].checkModem), this, 0, 5000));
            }

            Console.WriteLine("");
        }

        public class ControlWriter : TextWriter
        {
            private System.Windows.Forms.TextBox textbox;
            delegate void SetTextCallback(char text);

            public ControlWriter(System.Windows.Forms.TextBox textbox)
            {
                this.textbox = textbox;
            }

            public override void Write(char value)
            {
                if(this.textbox.InvokeRequired)
                {
                    SetTextCallback d = new SetTextCallback(Write);
                    textbox.Invoke(d, new object[] { value });
                }
                else
                {
                    //textbox.Text += value;
                    textbox.AppendText(value.ToString());
                }

            }

            public override void Write(string value)
            {
                textbox.Text += value;
            }

            public override Encoding Encoding
            {
                get { return Encoding.ASCII; }
            }
        }
    }

    public class SysHydro
    {
        public string Adress = "";
        public int Port = 2332;
        public int Period = 30;
        public string DB_Path = "";
        //static FbConnection databaseconnnection = null;

        public void checkModem(object e)
        {
            //Console.WriteLine(DateTime.Now.ToString() + " Check Status on : " + this.Adress + ":" + this.Port.ToString());

            float Voltage = 0;

            float lng = 0;
            float lat = 0;
            int fix = 0;
            int satellite_count = 0;

            string gps_raw_data;
            string voltage_raw_data;

            string s_connection = "User=SYSDBA;" +
                    "Password=masterkey;" +
                    "Database=" + this.DB_Path + ";" +
                    "DataSource=localhost;" +
                    "Port=3050;" +
                    "Dialect=3;" +
                    "Charset=ISO8859_1;";
            FbConnection databaseconnnection = new FbConnection(s_connection);
            SshClient Client = null;
            ShellStream shellStream= null;

            //Connexion au client et création du Shell
            try
            {
                Client = new SshClient(this.Adress, this.Port, "user", "continental21");
                Client.Connect();

                shellStream = Client.CreateShellStream("Terminal1", 100, 100, 1000, 1000, 500000);
                shellStream.Flush();

                //Envoi commande de GPS
                shellStream.WriteLine("at*gpsdata?");

                Thread.Sleep(1000);

                gps_raw_data = shellStream.Read().Replace("at*gpsdata?", "");

                //Envoi commande de Tension d'alim
                shellStream.WriteLine("at*powerin?");

                Thread.Sleep(1000);

                voltage_raw_data = shellStream.Read().Replace("at*powerin?", "");

                shellStream.Close();

                // Décorticage des data
                List<string> Lst_Data_GPS = Extract_Data_From_Shell(gps_raw_data);
                if (Lst_Data_GPS.Count == 4)
                {
                    int.TryParse(Lst_Data_GPS[0].Split('=')[1], out fix);
                    int.TryParse(Lst_Data_GPS[1].Split('=')[1], out satellite_count);
                    float.TryParse(Lst_Data_GPS[2].Split('=')[1], out lat);
                    float.TryParse(Lst_Data_GPS[3].Split('=')[1], out lng);
                }

                List<string> Lst_Data_Tension = Extract_Data_From_Shell(voltage_raw_data);
                if (Lst_Data_Tension.Count == 1)
                {
                    float.TryParse(Lst_Data_Tension[0], out Voltage);
                }

                if (Client.IsConnected)
                {
                    Client.Disconnect();
                }
                else { throw new Exception("Client " + this.Adress + " not Connected"); }

                DateTime rec = DateTime.UtcNow;
                Console.WriteLine(this.Adress + " - Time_Rec=" + rec.ToString() + "; Lat=" + lat.ToString() + "; Lng=" + lng.ToString() + "; Tension=" + Voltage.ToString());

                //Register Tension to BDD
                ////////////////////////////////////////////////////////////////////////////
                int j = 0;
                if (Voltage != 0)
                {
                    FbCommand insertTensionModemCommand = new FbCommand();
                    insertTensionModemCommand.Connection = databaseconnnection;

                    databaseconnnection.Open();

                    insertTensionModemCommand.CommandText = "INSERT INTO TENSION_MODEM (TIME_REC, TENSION)" +
                        "VALUES (@TIME_REC, @TENSION)";

                    insertTensionModemCommand.Parameters.Add("@TIME_REC", FbDbType.TimeStamp);
                    insertTensionModemCommand.Parameters.Add("@TENSION", FbDbType.Float);

                    insertTensionModemCommand.Transaction = databaseconnnection.BeginTransaction();

                    insertTensionModemCommand.Parameters[j++].Value = rec;
                    insertTensionModemCommand.Parameters[j++].Value = Voltage;

                    try
                    {
                        insertTensionModemCommand.ExecuteNonQuery();
                        // Commit changes
                        insertTensionModemCommand.Transaction.Commit();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Source + " : " + ex.Message);
                    }

                }

                //Register GPS_MODEM to BDD
                if(lat != 0 || lng != 0) {
                    FbCommand insertGPS_ModemCommand = new FbCommand();
                    insertGPS_ModemCommand.Connection = databaseconnnection;
                    insertGPS_ModemCommand.CommandText = "INSERT INTO GPS_MODEM (TIME_REC, lAT, LNG, QUALITY, NBSAT)" +
                                                        "VALUES ( @TIME_REC,@lAT, @LNG, @QUALITY, @NBSAT)";
                    insertGPS_ModemCommand.Parameters.Add("@TIME_REC", FbDbType.TimeStamp);
                    insertGPS_ModemCommand.Parameters.Add("@LAT", FbDbType.Float);
                    insertGPS_ModemCommand.Parameters.Add("@LNG", FbDbType.Float);
                    insertGPS_ModemCommand.Parameters.Add("@QUALITY", FbDbType.Float);
                    insertGPS_ModemCommand.Parameters.Add("@NBSAT", FbDbType.Float);

                    insertGPS_ModemCommand.Transaction = databaseconnnection.BeginTransaction();

                    j = 0;
                    insertGPS_ModemCommand.Parameters[j++].Value = rec;
                    insertGPS_ModemCommand.Parameters[j++].Value = lat;
                    insertGPS_ModemCommand.Parameters[j++].Value = lng;
                    insertGPS_ModemCommand.Parameters[j++].Value = -1;
                    insertGPS_ModemCommand.Parameters[j++].Value = satellite_count;

                    try
                    {
                        insertGPS_ModemCommand.ExecuteNonQuery();
                        // Commit changes
                        insertGPS_ModemCommand.Transaction.Commit();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Source + " : " + ex.Message);
                    }

                }

                databaseconnnection.Close();

            }
            catch (Exception ex)
            {
                Console.WriteLine("Error GetModemStatus " + this.Adress + " : " + ex.ToString());            
            }
            

        }

        public static List<string> Extract_Data_From_Shell(string text)
        {
            List<string> data = new List<string>();

            string[] semi_sorted = text.Split('\n');

            for (int i = 0; i < semi_sorted.Length; i++)
            {
                semi_sorted[i] = semi_sorted[i].Replace("\r", "").Replace("\n", "");

                if (semi_sorted[i] != "OK" && semi_sorted[i] != "")
                {
                    data.Add(semi_sorted[i]);
                }
            }

            return data;
        }

    }

}
