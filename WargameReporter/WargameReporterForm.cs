using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Net.Sockets;
using System.Net;
using System.IO;
using System.Diagnostics;
using System.Linq;

namespace WargameReporter
{
    public enum Protocol
    {
        TCP = 6,
        UDP = 17,
        Unknown = -1
    };

    public partial class WargameReporterForm : Form
    {
        private Socket mainSocket;                          //The socket which captures all incoming packets
        private byte[] byteData = new byte[4096];
        private bool bContinueCapturing = false;            //A flag to check if packets are to be captured or not

        private Process[] pname;

        public WargameReporterForm()
        {
            InitializeComponent();
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            if (cmbInterfaces.Text == "")
            {
                MessageBox.Show("Select an Interface to capture the packets.", "WargameReporter", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            try
            {
                if (!bContinueCapturing)        
                {
                    pname = Process.GetProcessesByName("Wargame3");
                    if (pname.Length == 0 && txtPath.Text != String.Empty)
                    {
                        Process.Start(txtPath.Text);
                        System.Threading.Thread.Sleep(1000);
                    }
                    pname = Process.GetProcessesByName("Wargame3");
                    if (pname.Length == 0)
                    {
                        MessageBox.Show("Wargame is not running and could not be started using the path provided.", "WargameReporter",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    
                    //Start capturing the packets...

                    btnStart.Text = "&Stop";

                    bContinueCapturing = true;

                    //For sniffing the socket to capture the packets has to be a raw socket, with the
                    //address family being of type internetwork, and protocol being IP
                    mainSocket = new Socket(AddressFamily.InterNetwork,
                        SocketType.Raw, ProtocolType.IP);
                    
                    //Bind the socket to the selected IP address
                    mainSocket.Bind(new IPEndPoint(IPAddress.Parse(cmbInterfaces.Text), 0));

                    //Set the socket  options
                    mainSocket.SetSocketOption(SocketOptionLevel.IP,            //Applies only to IP packets
                                               SocketOptionName.HeaderIncluded, //Set the include the header
                                               true);                           //option to true

                    byte[] byTrue = new byte[4] {1, 0, 0, 0};
                    byte[] byOut = new byte[4]{1, 0, 0, 0}; //Capture outgoing packets

                    //Socket.IOControl is analogous to the WSAIoctl method of Winsock 2
                    mainSocket.IOControl(IOControlCode.ReceiveAll,              //Equivalent to SIO_RCVALL constant
                                                                                //of Winsock 2
                                         byTrue,                                    
                                         byOut);

                    //Start receiving the packets asynchronously
                    mainSocket.BeginReceive(byteData, 0, byteData.Length, SocketFlags.None,
                        new AsyncCallback(OnReceive), null);
                }
                else
                {
                    btnStart.Text = "&Start";
                    bContinueCapturing = false;
                    //To stop capturing the packets close the socket
                    mainSocket.Close ();
                }
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                MessageBox.Show("Couldn't read data.  Are you running this program as an administrator?", "Wargame Reporter", MessageBoxButtons.OK, MessageBoxIcon.Error);
                btnStart.Text = "&Start";
                bContinueCapturing = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Wargame Reporter", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnReceive(IAsyncResult ar)
        {
            try
            {
                int nReceived = mainSocket.EndReceive(ar);

                //Analyze the bytes received...
                ParseData (byteData, nReceived);

            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            } finally
            {
                if (bContinueCapturing)
                {
                    byteData = new byte[4096];

                    //Another call to BeginReceive so that we continue to receive the incoming
                    //packets
                    mainSocket.BeginReceive(byteData, 0, byteData.Length, SocketFlags.None,
                        new AsyncCallback(OnReceive), null);
                }

            }
        }

        private void ParseData(byte[] byteData, int nReceived)
        {
            // Is wargame3.exe running?  If not, close application...
            pname = Process.GetProcessesByName("Wargame3");
            if(pname.Length == 0)
            {
                bContinueCapturing = false;
                mainSocket.Close();
                Application.Exit();
                return;
            }

            //Since all protocol packets are encapsulated in the IP datagram
            //so we start by parsing the IP header and see what protocol data
            //is being carried by it
            IPHeader ipHeader = new IPHeader(byteData, nReceived);

            if (ipHeader.DestinationAddress.ToString() == "87.98.143.182")
            {
                if(ipHeader.ProtocolType == Protocol.TCP)
                {
                    TCPHeader tcpHeader = new TCPHeader(ipHeader.Data, ipHeader.MessageLength);
                    if (tcpHeader.DestinationPort == "10280" || tcpHeader.DestinationPort == "10810")
                    {
                        byte[] data = new byte[tcpHeader.MessageLength];
                        Array.Copy(tcpHeader.Data, 0, data, 0, tcpHeader.MessageLength);
                        string decode = BitConverter.ToString(data).Replace("-","");
                        if (decode.Substring(0, 30) == "474554202F7374617473322F75305F")
                        {
                            byte[] postData = Encoding.UTF8.GetBytes("data=" + decode);
                            PostWebRequest("http://europeinruins2-com.securec64.ezhostingserver.com/wargame/svc/getUser.cfm", postData);
                        } else if (decode.Substring(4,16) == "C67B2267616D6522")
                        {
                            TimeSpan t = DateTime.UtcNow - new DateTime(1970, 1, 1);
                            string secondsSinceEpoch = Convert.ToString((int)t.TotalSeconds);
                            byte[] postData = Encoding.UTF8.GetBytes("timestamp=" + secondsSinceEpoch + "&data=" + decode);
                            PostWebRequest("http://europeinruins2-com.securec64.ezhostingserver.com/wargame/svc/getGame.cfm", postData);
                        }
                    }
                }
            }
        }

        private void PostWebRequest(string url, byte[] data)
        {
            HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(url);
            webRequest.Method = "POST";
            webRequest.ContentType = "application/x-www-form-urlencoded";
            webRequest.ContentLength = data.Length;
            webRequest.BeginGetRequestStream(new AsyncCallback(GetRequestStreamCallback), Tuple.Create(webRequest, data));
        }


        private void GetRequestStreamCallback(IAsyncResult asynchronousResult)
        {
            Tuple<HttpWebRequest, byte[]> state = (Tuple<HttpWebRequest, byte[]>)asynchronousResult.AsyncState;
            HttpWebRequest webRequest = state.Item1;
            byte[] data = state.Item2;
            Stream postStream = webRequest.EndGetRequestStream(asynchronousResult);
            postStream.Write(data, 0, data.Length);
            postStream.Close();
            webRequest.BeginGetResponse(new AsyncCallback(GetResponseCallback), webRequest);
        }

        private void GetResponseCallback(IAsyncResult asynchronousResult)
        {
            HttpWebRequest request = (HttpWebRequest)asynchronousResult.AsyncState;

            // End the operation
            HttpWebResponse response = (HttpWebResponse)request.EndGetResponse(asynchronousResult);
            Stream streamResponse = response.GetResponseStream();
            StreamReader streamRead = new StreamReader(streamResponse);
            string responseString = streamRead.ReadToEnd();
            Console.WriteLine(responseString);
            // Close the stream object
            streamResponse.Close();
            streamRead.Close();

            // Release the HttpWebResponse
            response.Close();
        }

        private void SnifferForm_Load(object sender, EventArgs e)
        {
            txtPath.Text = Properties.Settings.Default.txtPath;
            
            string strIP = null;

            IPAddress[] ipv4Addresses = Array.FindAll(
                Dns.GetHostEntry(string.Empty).AddressList,
                a => a.AddressFamily == AddressFamily.InterNetwork);

            if (ipv4Addresses.Length > 0)
            {
                foreach (IPAddress ip in ipv4Addresses)
                {
                    strIP = ip.ToString();
                    cmbInterfaces.Items.Add(strIP);
                }
                if (ipv4Addresses.Length == 1)
                {
                    cmbInterfaces.SelectedIndex = 0;
                }
                else
                {
                    cmbInterfaces.SelectedIndex = Properties.Settings.Default.interfaceIndex;
                }
            }

            pname = Process.GetProcessesByName("Wargame3");
            if ((pname.Length != 0 || txtPath.Text != String.Empty) && cmbInterfaces.SelectedIndex >= 0)
            {
                btnStart.PerformClick();
            }
            else if (cmbInterfaces.SelectedIndex < 0)
            {
                MessageBox.Show("Your system has multiple IPv4 addresses.  You must pick the one your internet traffic flows through for Wargame Reporter to work.");
            }

        }

        private void SnifferForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (bContinueCapturing)
            {
                mainSocket.Close();
            }
        }

        private void btnBrowse_Click(object sender, EventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();

            dialog.Filter = "Wargame3|Wargame3.exe|All files (*.*)|*.*";

            dialog.InitialDirectory = "C:";

            dialog.Title = "Select the Wargame3 executable... (Ex: C:\\Program Files\\steamapps\\common\\Wargame Red Dragon\\Wargame3.exe";

            if (dialog.ShowDialog() == DialogResult.OK)
                txtPath.Text = dialog.FileName;

            if (dialog.FileName == String.Empty)
                txtPath.Text = String.Empty;

            Properties.Settings.Default.txtPath = txtPath.Text;
            Properties.Settings.Default.Save();

        }

        private void cmbInterfaces_SelectedIndexChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.interfaceIndex = cmbInterfaces.SelectedIndex;
            Properties.Settings.Default.Save();
        }


        private void WargameReporterForm_Resize(object sender, EventArgs e)
        {
            if (FormWindowState.Minimized == this.WindowState)
            {
                notifyIcon.Visible = true;
                notifyIcon.ShowBalloonTip(500);
                this.ShowInTaskbar = false;
            }

        }

        private void notifyIcon_DoubleClick(object sender, EventArgs e)
        {
            this.WindowState = FormWindowState.Normal;
            this.ShowInTaskbar = true;
            notifyIcon.Visible = false;
        }


    }
}