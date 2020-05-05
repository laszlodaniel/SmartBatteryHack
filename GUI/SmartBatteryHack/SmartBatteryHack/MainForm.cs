using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Timers;
using System.Windows.Forms;

namespace SmartBatteryHack
{
    public partial class MainForm : Form
    {
        public string DateTimeNow;
        public static string USBLogFilename;
        public static string USBBinaryLogFilename;
        public static string ROMBinaryFilename;
        public bool SerialPortAvailable = false;
        public bool Timeout = false;
        public bool DeviceFound = false;
        public List<byte> bufferlist = new List<byte>();
        public List<ushort> SMBusRegisterDumpList = new List<ushort>();
        public ushort ChipID = 0;
        public string Chip;
        public string SelectedPort = String.Empty;

        public byte[] HandshakeRequest = new byte[] { 0x3D, 0x00, 0x02, 0x01, 0x00, 0x03 };
        public byte[] ExpectedHandshake = new byte[] { 0x3D, 0x00, 0x08, 0x81, 0x00, 0x53, 0x42, 0x48, 0x41, 0x43, 0x4B, 0x35 };
        public byte[] CurrentSettingsRequest = new byte[] { 0x3D, 0x00, 0x02, 0x03, 0x01, 0x06 };

        AboutForm about;
        SerialPort Serial = new SerialPort();
        System.Timers.Timer TimeoutTimer = new System.Timers.Timer();

        public MainForm()
        {
            InitializeComponent();
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            // Create LOG directory if it doesn't exist
            if (!Directory.Exists("LOG")) Directory.CreateDirectory("LOG");
            if (!Directory.Exists("ROMs")) Directory.CreateDirectory("ROMs");

            // Set logfile names inside the LOG directory
            DateTimeNow = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            USBLogFilename = @"LOG/usblog_" + DateTimeNow + ".txt";
            USBBinaryLogFilename = @"LOG/usblog_" + DateTimeNow + ".bin";
            ROMBinaryFilename = @"ROMs/rom_" + DateTimeNow + ".bin";

            UpdateCOMPortList();

            // Setup timeout timer
            TimeoutTimer.Elapsed += new ElapsedEventHandler(TimeoutHandler);
            TimeoutTimer.Interval = 2000; // ms
            TimeoutTimer.Enabled = false;

            for (int i = 0; i < 256; i++)
            {
                ReadRegisterComboBox.Items.Add(Convert.ToString(i, 16).PadLeft(2, '0').ToUpper());
                WriteRegisterComboBox.Items.Add(Convert.ToString(i, 16).PadLeft(2, '0').ToUpper());
            }

            ReadRegisterComboBox.SelectedIndex = 0;
            WriteRegisterComboBox.SelectedIndex = 0;
            WordByteOrderComboBox.SelectedIndex = 3;

            ActiveControl = ConnectButton; // put focus on the connect button
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (DeviceFound) ConnectButton.PerformClick(); // disconnect first
            if (Serial.IsOpen) Serial.Close();
            Application.DoEvents();
        }

        private void UpdateCOMPortList()
        {
            COMPortsComboBox.Items.Clear(); // clear combobox
            string[] ports = SerialPort.GetPortNames(); // get available ports

            if (ports.Length > 0)
            {
                COMPortsComboBox.Items.AddRange(ports);
                SerialPortAvailable = true;
                ConnectButton.Enabled = true;

                if (SelectedPort == String.Empty) // if no port has been selected
                {
                    COMPortsComboBox.SelectedIndex = 0; // select first available port
                    SelectedPort = COMPortsComboBox.Text;
                }
                else
                {
                    try
                    {
                        COMPortsComboBox.SelectedIndex = COMPortsComboBox.Items.IndexOf(SelectedPort);
                    }
                    catch
                    {
                        COMPortsComboBox.SelectedIndex = 0;
                    }
                }
            }
            else
            {
                COMPortsComboBox.Items.Add("N/A");
                SerialPortAvailable = false;
                ConnectButton.Enabled = false;
                COMPortsComboBox.SelectedIndex = 0; // select "N/A"
                SelectedPort = String.Empty;
                Util.UpdateTextBox(CommunicationTextBox, "[INFO] No device available", null);
            }
        }

        private void COMPortsComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            SelectedPort = COMPortsComboBox.Text;
        }

        private void RefreshButton_Click(object sender, EventArgs e)
        {
            UpdateCOMPortList();
        }

        private void TimeoutHandler(object source, ElapsedEventArgs e)
        {
            Timeout = true;
        }

        private void ConnectButton_Click(object sender, EventArgs e)
        {
            if (!DeviceFound) // connect
            {
                UpdateCOMPortList();

                if (SerialPortAvailable)
                {
                    byte[] buffer = new byte[2048];
                    byte ConnectionCounter = 0;
                    
                    while (ConnectionCounter < 5) // try connecting to the device 5 times, then give up
                    {
                        ConnectButton.Enabled = false; // no double-click

                        if (Serial.IsOpen) Serial.Close(); // can't overwrite fields if serial port is open
                        Serial.PortName = COMPortsComboBox.Text;
                        Serial.BaudRate = 250000;
                        Serial.DataBits = 8;
                        Serial.StopBits = StopBits.One;
                        Serial.Parity = Parity.None;
                        Serial.ReadTimeout = 500;
                        Serial.WriteTimeout = 500;

                        Util.UpdateTextBox(CommunicationTextBox, "[INFO] Connecting to " + Serial.PortName, null);

                        try
                        {
                            Serial.Open(); // open current serial port
                        }
                        catch
                        {
                            Util.UpdateTextBox(CommunicationTextBox, "[INFO] " + Serial.PortName + " is opened by another application", null);
                            Util.UpdateTextBox(CommunicationTextBox, "[INFO] Device not found at " + Serial.PortName, null);
                            break;
                        }

                        if (Serial.IsOpen)
                        {
                            Serial.DiscardInBuffer();
                            Serial.DiscardOutBuffer();
                            Serial.BaseStream.Flush();

                            Util.UpdateTextBox(CommunicationTextBox, "[<-TX] Handshake request (" + Serial.PortName + ")", HandshakeRequest);
                            Serial.Write(HandshakeRequest, 0, HandshakeRequest.Length);

                            Timeout = false;
                            TimeoutTimer.Enabled = true;

                            while (!Timeout)
                            {
                                if (Serial.BytesToRead > 11)
                                {
                                    Serial.Read(buffer, 0, 12);
                                    break;
                                }
                            }

                            TimeoutTimer.Enabled = false;

                            Serial.DiscardInBuffer();
                            Serial.DiscardOutBuffer();
                            Serial.BaseStream.Flush();

                            if (Timeout)
                            {
                                Util.UpdateTextBox(CommunicationTextBox, "[INFO] Device is not responding at " + Serial.PortName, null);
                                Timeout = false;
                                Serial.Close();
                                ConnectionCounter++; // increase counter value and try again
                            }
                            else
                            {
                                DeviceFound = Util.CompareArrays(buffer, ExpectedHandshake, 0, 12);

                                if (DeviceFound)
                                {
                                    Util.UpdateTextBox(CommunicationTextBox, "[RX->] Handshake response", ExpectedHandshake);
                                    Util.UpdateTextBox(CommunicationTextBox, "[INFO] Handshake OK: SBHACK", null);
                                    Util.UpdateTextBox(CommunicationTextBox, "[INFO] Device connected (" + Serial.PortName + ")", null);
                                    ConnectButton.Text = "Disconnect";
                                    StatusButton.Enabled = true;
                                    ResetButton.Enabled = true;
                                    ToolsGroupBox.Enabled = true;
                                    Serial.DataReceived += new SerialDataReceivedEventHandler(SerialDataReceivedHandler);
                                    Serial.Write(CurrentSettingsRequest, 0, CurrentSettingsRequest.Length);
                                    break; // exit while-loop
                                }
                                else
                                {
                                    Util.UpdateTextBox(CommunicationTextBox, "[RX->] Data received", buffer.Take(12).ToArray());
                                    Util.UpdateTextBox(CommunicationTextBox, "[INFO] Handshake ERROR: " + Encoding.ASCII.GetString(buffer, 5, 6), null);
                                    Util.UpdateTextBox(CommunicationTextBox, "[INFO] Device not found at " + Serial.PortName, null);
                                    Serial.Close();
                                    ConnectionCounter++; // increase counter value and try again
                                }
                            }
                        }
                    }

                    ConnectButton.Enabled = true;
                }
            }
            else // disconnect
            {
                if (Serial.IsOpen)
                {
                    Serial.DiscardInBuffer();
                    Serial.DiscardOutBuffer();
                    Serial.BaseStream.Flush();
                    Serial.Close();
                    DeviceFound = false;
                    ConnectButton.Text = "Connect";
                    StatusButton.Enabled = false;
                    ResetButton.Enabled = false;
                    ToolsGroupBox.Enabled = false;
                    Util.UpdateTextBox(CommunicationTextBox, "[INFO] Device disconnected (" + Serial.PortName + ")", null);
                    Serial.DataReceived -= new SerialDataReceivedEventHandler(SerialDataReceivedHandler);
                }
            }
        }

        private void SerialDataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort sp = (SerialPort)sender;
            int DataLength = sp.BytesToRead;

            // This approach enables reading multiple broken transmissions
            // First just add the received bytes to a global list
            for (int i = 0; i < DataLength; i++)
            {
                try
                {
                    bufferlist.Add((byte)sp.ReadByte());
                }
                catch
                {
                    Util.UpdateTextBox(CommunicationTextBox, "[INFO] Serial read error", null);
                    break;
                }
            }

            // Multiple packets are handled one after another in this while-loop
            while (bufferlist.Count > 0)
            {
                if (bufferlist[0] == 0x3D)
                {
                    if (bufferlist.Count < 3) break; // wait for the length bytes

                    int PacketLength = (bufferlist[1] << 8) + bufferlist[2];
                    int FullPacketLength = PacketLength + 4;

                    if (bufferlist.Count < FullPacketLength) break; // wait for the rest of the bytes to arrive

                    byte[] Packet = new byte[FullPacketLength];
                    int PayloadLength = PacketLength - 2;
                    byte[] Payload = new byte[PayloadLength];
                    int ChecksumLocation = PacketLength + 3;
                    byte DataCode = 0;
                    byte Source = 0;
                    byte Command = 0;
                    byte SubDataCode = 0;
                    byte Checksum = 0;
                    byte CalculatedChecksum = 0;

                    Array.Copy(bufferlist.ToArray(), 0, Packet, 0, Packet.Length);

                    Checksum = Packet[ChecksumLocation]; // get packet checksum byte

                    for (int i = 1; i < ChecksumLocation; i++)
                    {
                        CalculatedChecksum += Packet[i]; // calculate checksum
                    }

                    if (CalculatedChecksum == Checksum) // verify checksum
                    {
                        DataCode = Packet[3];
                        Source = (byte)((DataCode >> 7) & 0x01);
                        Command = (byte)(DataCode & 0x0F);
                        SubDataCode = Packet[4];

                        if (PayloadLength > 0) // copy payload bytes if available
                        {
                            Array.Copy(Packet, 5, Payload, 0, PayloadLength);
                        }

                        if (Source == 1) // highest bit set in the DataCode byte means the packet is coming from the device
                        {
                            switch (Command) // based on the datacode decide what to do with this packet
                            {
                                case 0x00: // reset
                                    switch (SubDataCode)
                                    {
                                        case 0x00:
                                            Util.UpdateTextBox(CommunicationTextBox, "[RX->] Device is resetting, please wait...", Packet);
                                            break;
                                        case 0x01:
                                            Util.UpdateTextBox(CommunicationTextBox, "[RX->] Device is ready", Packet);
                                            break;
                                        default:
                                            Util.UpdateTextBox(CommunicationTextBox, "[RX->] Unknown reset packet", Packet);
                                            break;
                                    }
                                    break;
                                case 0x01: // handshake
                                    Util.UpdateTextBox(CommunicationTextBox, "[RX->] Handshake received", Packet);
                                    if (Encoding.ASCII.GetString(Payload, 0, Payload.Length) == "SBHACK") Util.UpdateTextBox(CommunicationTextBox, "[INFO] Handshake OK: SBHACK", null);
                                    else Util.UpdateTextBox(CommunicationTextBox, "[INFO] Handshake ERROR: " + Encoding.ASCII.GetString(Payload, 0, Payload.Length), null);
                                    break;
                                case 0x02: // status
                                    switch (SubDataCode)
                                    {
                                        case 0x01: // timestamp
                                            Util.UpdateTextBox(CommunicationTextBox, "[RX->] Timestamp received", Packet);
                                            if (Payload.Length > 3)
                                            {
                                                TimeSpan ElapsedTime = TimeSpan.FromMilliseconds(Payload[0] << 24 | Payload[1] << 16 | Payload[2] << 8 | Payload[3]);
                                                DateTime Timestamp = DateTime.Today.Add(ElapsedTime);
                                                string TimestampString = Timestamp.ToString("HH:mm:ss.fff");
                                                Util.UpdateTextBox(CommunicationTextBox, "[INFO] Timestamp: " + TimestampString, null);
                                            }
                                            break;
                                        case 0x02: // scan SMBus address result
                                            Util.UpdateTextBox(CommunicationTextBox, "[RX->] Scan SMBus address result", Packet);
                                            if ((Payload.Length > 0) && (Payload[0] != 0xFF))
                                            {
                                                string SmartBatteryAddressList = Util.ByteToHexString(Payload, 0, Payload.Length);
                                                Util.UpdateTextBox(CommunicationTextBox, "[INFO] SMBus device(s): " + SmartBatteryAddressList, null);

                                                SMBusAddressComboBox.BeginInvoke((MethodInvoker)delegate
                                                {
                                                    SMBusAddressComboBox.Items.Clear();
                                                    for (int i = 0; i < Payload.Length; i++)
                                                    {
                                                        SMBusAddressComboBox.Items.Add(Util.ByteToHexString(Payload, i, i + 1));
                                                    }
                                                    SMBusAddressComboBox.SelectedIndex = 0;
                                                    SMBusAddressSelectButton.Enabled = true;
                                                });
                                            }
                                            else
                                            {
                                                Util.UpdateTextBox(CommunicationTextBox, "[INFO] No SMBus device found", null);

                                                SMBusAddressComboBox.BeginInvoke((MethodInvoker)delegate
                                                {
                                                    SMBusAddressComboBox.Items.Clear();
                                                    SMBusAddressComboBox.Items.Add("--");
                                                    SMBusAddressSelectButton.Enabled = false;
                                                });
                                            }
                                            break;
                                        case 0x03: // smbus register dump
                                            Util.UpdateTextBox(CommunicationTextBox, "[RX->] SMBus register dump (" + Util.ByteToHexString(Payload, 0, 1) + "-" + Util.ByteToHexString(Payload, 1, 2) + ")", Packet);
                                            if (Payload.Length > 2)
                                            {
                                            	StringBuilder value = new StringBuilder();
												ushort current_byte=2; //current byte in payload 0:start reg 1:final reg from 2 to endding are each reg and data 
                                                byte current_reg = 0; //current reg
                                                byte[] data = new byte[32]; //32 bytes for byte-by-byte data of current reg
												byte data_lenth; //if the reg data is block, the size of the string
												string datastring; //if ASCII string block, the string of reg value
												ushort dataword; //the value of reg value
												if (Payload[1] >= 0x40) 
													value.AppendLine("--CAUTIOUS! Lookup datasheet for details of Ext Reg. Value 0x1717 maybe an error when reading reg. Read it individually and dump reg 0x16 to view error code--");
                                                for (current_reg = Payload[0]; current_reg <= Payload[1]; current_reg++)
                                                {

													switch (current_reg)
													{
														case 0x00: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //trun to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word with HEX format
																value.Append("ManufacturerAccess: " + Util.ByteToHexString(data, 0, 2));
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x01: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("RemainingCapacityAlarm: " + dataword.ToString() + " mAh"); //decoding, show in DEC format
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x02: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("RemainingTimeAlarm: " + dataword.ToString() + " minuets"); //decoding, show in DEC format
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x03: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("BatteryMode: "); //decoding, show in BIN format
																if ((dataword & 0x8000) == 0)
																    value.Append("Report in mA or mAh (default). ");
																else
																	value.Append("Report in 10mW or 10mWh. ");
																
																if ((dataword & 0x4000) == 0)
																    value.Append("Enable ChargingVoltage and ChargingCurrent broadcasts to Charger (default). ");
																else
																	value.Append("Disable ChargingVoltage and ChargingCurrent broadcasts to Charger. ");
																
																if ((dataword & 0x2000) == 0)
																    value.Append("Enable AlarmWarning broadcasts to Host and Charger (default). ");
																else
																	value.Append("Disable AlarmWarning broadcasts to Host and Charger. ");
																
																if ((dataword & 0x80) == 0)
																    value.Append("Battery OK. ");
																else
																	value.Append("Capacity Re-Learn Cycle Requested. ");
																
																if ((dataword & 0x2) == 0)
																    value.Append("Primary or Secondary Battery Not Supported. ");
																else if ((dataword & 0x200) == 0)
																	    value.Append("Battery in secondary role (default). ");
																	 else
																		value.Append("Battery in primary role. ");
																
																if ((dataword & 0x1) == 0)
																    value.Append("Internal Charge Controller Not Supported.");
																else if ((dataword & 0x100) == 0)
																		value.Append("Internal Charge Control Disabled (default).");
																	 else
																		value.Append("Internal Charge Control Enabled.");
																
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x04: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("AtRate: " + ((short)dataword).ToString() + " mAh"); //decoding, show in DEC format
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x05: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("AtRateTimeToFull: " + dataword.ToString() + " minuets"); //decoding, show in DEC format
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x06: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("AtRateTimeToEmpty: " + dataword.ToString() + " minuets"); //decoding, show in DEC format
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x07: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("AtRateOK: " + Convert.ToBoolean(dataword)); //decoding, show in Boolean format
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x08: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Temperature: " + (dataword/10.0-273).ToString() + " °C"); //decoding, show in DEC format
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x09: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Voltage: " + dataword.ToString() + " mV"); //decoding, show in DEC format
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x0A: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Current: " + dataword.ToString() + " mA"); //decoding, show in DEC format
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x0B: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("AverageCurrent: " + dataword.ToString() + " mA"); //decoding, show in DEC format
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x0C: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("MaxError: " + dataword.ToString() + "%"); //decoding, show in DEC format
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x0D: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("RelativeStateOfCharge: " + dataword.ToString() + "%"); //decoding, show in DEC format
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x0E: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("AbsoluteStateOfCharge: " + dataword.ToString() + "%"); //decoding, show in DEC format
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x0F: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("RemainingCapacity: " + dataword.ToString() + " mAh"); //decoding, show in DEC format
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x10: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("FullChargeCapacity: " + dataword.ToString() + " mAh"); //decoding, show in DEC format
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x11: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("RunTimeToEmpty: " + dataword.ToString() + " minuets"); //decoding, show in DEC format
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x12: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("AverageTimeToEmpty: " + dataword.ToString() + " minuets"); //decoding, show in DEC format
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x13: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("AverageTimeToFull: " + dataword.ToString() + " minuets"); //decoding, show in DEC format
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x14: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("ChargingCurrent: " + dataword.ToString() + " mA"); //decoding, show in DEC format
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x15: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("ChargingVoltage: " + dataword.ToString() + " mV"); //decoding, show in DEC format
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x16: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("BatteryStatus: "); //decoding, show in DEC format
																if ((dataword & 0xDB00) != 0) value.Append("ALARMS: ");
																if ((dataword & 0x8000) != 0) value.Append("OVER_CHARGED ");
																if ((dataword & 0x4000) != 0) value.Append("TERMINATE_CHARGE ");
																if ((dataword & 0x1000) != 0) value.Append("OVER_TEMP ");
																if ((dataword & 0x800) != 0) value.Append("TERMINATE_DISCHARGE ");
																if ((dataword & 0x200) != 0) value.Append("REMAINING_CAPACITY ");
																if ((dataword & 0x100) != 0) value.Append("REMAINING_TIME ");
																if ((dataword & 0xF0) != 0) value.Append("STATUS: ");
																if ((dataword & 0x80) != 0) value.Append("INITIALIZED ");
																if ((dataword & 0x40) != 0) value.Append("DISCHARGING ");
																if ((dataword & 0x20) != 0) value.Append("FULLY_CHARGED ");
																if ((dataword & 0x10) != 0) value.Append("FULLY_DISCHARGED ");
																if ((dataword & 0xF) != 0) value.Append("ERRORS: ");
																switch (dataword & 0xF)
																{
																	case 0x7: value.Append("UnknownError "); break;
																	case 0x6: value.Append("BadSize "); break;
																	case 0x5: value.Append("Overflow/Underflow "); break;
																	case 0x4: value.Append("AccessDenied "); break;
																	case 0x3: value.Append("UnsupportedCommand "); break;
																	case 0x2: value.Append("ReservedCommand "); break;
																	case 0x1: value.Append("Busy "); break;
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x17: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("CycleCount: " + dataword.ToString() + " Cycles"); //decoding, show in DEC format
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x18: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("DesignCapacity: " + dataword.ToString() + " mAh"); //decoding, show in DEC format
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x19: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("DesignVoltage: " + dataword.ToString() + " mV"); //decoding, show in DEC format
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x1A: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("SpecificationInfo: "  ); //decoding, show in DEC format
																switch (dataword & 0xFF)
																{
																	case 0x10: value.Append("Smart Battery Spec 1.0"); break;
																	case 0x21: value.Append("Smart Battery Spec 1.1 without PEC"); break;
																	case 0x31: value.Append("Smart Battery Spec 1.1 with PEC"); break;
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x1B: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																int year = 1980 + (dataword >> 9 & 0x7f); int month = dataword >> 5 & 0xf; int day = dataword & 0x1f;
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("ManufactureDate: " + "Y" + year + "M" + month + "D"+ day ); //decoding, show in DEC format
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x1C: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("SerialNumber: " + dataword.ToString()); //decoding, show in DEC format
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x1D: //HEX word
															goto case 0x3B;
														case 0x1E: //HEX word
															goto case 0x3B;
														case 0x1F: //HEX word
															goto case 0x3B;
														case 0x20: //ASCII string
															if(current_reg == Payload[current_byte])
															{
																data_lenth = Payload[current_byte + 1]; //get register block data size
																Array.Copy(Payload, current_byte + 2, data, 0, data_lenth); //get register data
																datastring = Encoding.ASCII.GetString(data, 0, data_lenth); //to ASCII string
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, data_lenth) + " // "); //show the block in HEX format
																value.Append("ManufacturerName: " + datastring); //decoding, show in ASCII string format
																current_byte += (ushort)(2 + data_lenth); //move index to next reg
															}
															break;
														case 0x21: //ASCII string
															if(current_reg == Payload[current_byte])
															{
																data_lenth = Payload[current_byte+1]; //get register block data size
																Array.Copy(Payload, current_byte+2, data, 0, data_lenth); //get register data
																datastring = Encoding.ASCII.GetString(data, 0, data_lenth); //to ASCII string
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, data_lenth) + " // "); //show the block in HEX format
																value.Append("DeviceName: " + datastring); //decoding, show in ASCII string format
																current_byte += (ushort)(2 + data_lenth); //move index to next reg
															}
																break;
														case 0x22: //ASCII string
															if(current_reg == Payload[current_byte])
															{
																data_lenth = Payload[current_byte+1]; //get register block data size
																Array.Copy(Payload, current_byte+2, data, 0, data_lenth); //get register data
																datastring = Encoding.ASCII.GetString(data, 0, data_lenth); //to ASCII string
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, data_lenth) + " // "); //show the block in HEX format
																value.Append("DeviceChemisty: " + datastring); //decoding, show in ASCII string format
																current_byte += (ushort)(2 + data_lenth); //move index to next reg
															}
															break;
														case 0x23: //HEX string
															if(current_reg == Payload[current_byte])
															{
																data_lenth = Payload[current_byte+1]; //get register block data size
																Array.Copy(Payload, current_byte+2, data, 0, data_lenth); //get register data
																datastring = Util.ByteToHexString(data, 0, data_lenth); //to HEX string
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, data_lenth) + " // "); //show the block in HEX format
																value.Append("ManufacturerData: " + datastring); //decoding, show in HEX string format
																current_byte += (ushort)(2 + data_lenth); //move index to next reg
															}
															break;
														case 0x24: //HEX word
															goto case 0x3B;
														case 0x25: //HEX word
															goto case 0x3B;
														case 0x26: //HEX word
															goto case 0x3B;
														case 0x27: //HEX word
															goto case 0x3B;
														case 0x28: //HEX word
															goto case 0x3B;
														case 0x29: //HEX word
															goto case 0x3B;
														case 0x2A: //HEX word
															goto case 0x3B;
														case 0x2B: //HEX word
															goto case 0x3B;
														case 0x2C: //HEX word
															goto case 0x3B;
														case 0x2D: //HEX word
															goto case 0x3B;
														case 0x2E: //HEX word
															goto case 0x3B;
														case 0x2F: //HEX string
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("OptionalMfgFunction5: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																	value.Append("BQ208X: Pack Status and Pack Configuration: ");
																	if ((dataword & 0x80) != 0) value.Append("System present. "); else value.Append("System unpresent. ");
																	if ((dataword & 0x40) != 0) value.Append("V≤EndDischargeVoltage2. "); else value.Append("V>EndDischargeVoltage2. ");
																	if ((dataword & 0x20) != 0) value.Append("Sealed. "); else value.Append("Unsealed. "); 
																	if ((dataword & 0x10) != 0) value.Append("Discharge cycle valid for an FCC update." ); else value.Append("Discharge cycle invalid for an FCC update. "); 
																	if ((dataword & 0x8) != 0) value.Append("AFE com failed. "); else value.Append("AFE com OK. ");
																	if ((dataword & 0x4) != 0) value.Append("PF Flag set. "); else value.Append("PF Flag clear. ");
																	if ((dataword & 0x2) != 0) value.Append("CellVoltageOverVoltage. ");
																		else if ((dataword & 0x1) != 0) value.Append("CellVoltageUnderVoltage. ");
																			else value.Append("CellVoltage OK. ");
																}
																else
																	value.Append("BQ20ZXX: Authenticate. Read " + data[1] + "-byte-block manually. ");

																current_byte += 3; //move index to next reg
															}
															break;
														case 0x30: //HEX word
															goto case 0x3B;
														case 0x31: //HEX word
															goto case 0x3B;
														case 0x32: //HEX word
															goto case 0x3B;
														case 0x33: //HEX word
															goto case 0x3B;
														case 0x34: //HEX word
															goto case 0x3B;
														case 0x35: //HEX word
															goto case 0x3B;
														case 0x36: //HEX word
															goto case 0x3B;
														case 0x37: //HEX word
															goto case 0x3B;
														case 0x38: //HEX word
															goto case 0x3B;
														case 0x39: //HEX word
															goto case 0x3B;
														case 0x3A: //HEX word
															goto case 0x3B;
														case 0x3B: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Reserved: " + dataword.ToString()); //decoding, show in DEC format
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x3C: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("OptionalMfgFunction4 (VCell4 for BQ): " + dataword.ToString() + " mV"); //decoding, show in DEC format
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x3D: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("OptionalMfgFunction3 (VCell3 for BQ): " + dataword.ToString() + " mV"); //decoding, show in DEC format
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x3E: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("OptionalMfgFunction2 (VCell2 for BQ): " + dataword.ToString() + " mV"); //decoding, show in DEC format
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x3F: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("OptionalMfgFunction1 (VCell1 for BQ): " + dataword.ToString() + " mV"); //decoding, show in DEC format
																current_byte += 3; //move index to next reg
															}
															break;
														
														case 0x45: //HEX string
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																	value.Append("BQ208X: VPack: " + dataword + " mV");
																}
																else if (Chip.Contains("20Z"))
																{
																	value.Append("BQ20ZXX: AFEData. Read " + data[1] + "-byte-block manually. ");
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x46: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																	value.Append("BQ208X: AFEData: ");
																	if ((dataword & 0x2000) != 0) value.Append("ZVCLMP "); 
																	if ((dataword & 0x1000) != 0) value.Append("SLEEPDET ");
																	if ((dataword & 0x800) != 0) value.Append("WDF ");
																	if ((dataword & 0x400) != 0) value.Append("OL ");
																	if ((dataword & 0x200) != 0) value.Append("SCCHG ");
																	if ((dataword & 0x100) != 0) value.Append("SCDSG ");
																	value.Append("For full message dump block manually.");
																}
																else if (Chip.Contains("20Z"))
																{
																	value.Append("BQ20ZXX: FETControl: ");
																	if ((dataword & 0x10) != 0) value.Append("AFE GPOD Enabled. "); else value.Append("AFE GPOD Disabled. "); 
																	if ((dataword & 0x8) != 0) value.Append("ZVCHG FET On. "); else value.Append("ZVCHG FET Off. ");
																	if ((dataword & 0x4) != 0) value.Append("CHG FET On. "); else value.Append("CHG FET Off. ");
																	if ((dataword & 0x2) != 0) value.Append("DSG FET On. "); else value.Append("DSG FET Off. ");
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x4F: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																}
																else if (Chip.Contains("20Z"))
																{
																	value.Append("BQ20ZXX: StateOfHealth: " + (dataword & 0xff).ToString() + "% "); //decoding, show in DEC format bq20z655/z40/z45
																	if ((dataword & 0x400) != 0) value.Append("Cell Life Limit. ");
																	if ((dataword & 0x200) != 0) value.Append("Deterioration Warning. ");
																	if ((dataword & 0x100) != 0) value.Append("Deterioration Fault. ");
																}
																else
																{
																}
																current_byte += 3; //move index to next reg

															}
															break;
														case 0x50: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																	value.Append("BQ208X: Word, Writing a byte to dataflash (address ≤ 0xff).");
																}
																else if (Chip.Contains("20Z"))
																{
																	value.Append("BQ20ZXX: SafetyAlert: "); //decoding, show in DEC format
																	if ((dataword & 0x8000) != 0) value.Append("Discharge OT. ");
																	if ((dataword & 0x4000) != 0) value.Append("Charge OT. ");
																	if ((dataword & 0x2000) != 0) value.Append("Discharge OC. ");
																	if ((dataword & 0x1000) != 0) value.Append("Charge OC. ");
																	if ((dataword & 0x800) != 0) value.Append("Tier-2 Discharge OC. ");
																	if ((dataword & 0x400) != 0) value.Append("Tier-2 Charge OC. ");
																	if ((dataword & 0x200) != 0) value.Append("Pack UV. ");
																	if ((dataword & 0x100) != 0) value.Append("Pack OV. ");
																	if ((dataword & 0x80) != 0) value.Append("Cell UV. ");
																	if ((dataword & 0x40) != 0) value.Append("Cell OV. ");
																	if ((dataword & 0x20) != 0) value.Append("PF. ");
																	if ((dataword & 0x10) != 0) value.Append("Host WD. ");
																	if ((dataword & 0x8) != 0) value.Append("AFE WD. ");
																	if ((dataword & 0x4) != 0) value.Append("Discharge OC. ");
																	if ((dataword & 0x2) != 0) value.Append("Charge SC. ");
																	if ((dataword & 0x1) != 0) value.Append("Discharge SC. ");
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x51: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																	value.Append("BQ208X: Word, Setting the address (≤ 0xff) of the dataflash byte to read. ");
																}
																else if (Chip.Contains("20Z"))
																{
																	value.Append("BQ20ZXX: SafetyStatus: "); //decoding, show in DEC format
																	if ((dataword & 0x8000) != 0) value.Append("Discharge OT. ");
																	if ((dataword & 0x4000) != 0) value.Append("Charge OT. ");
																	if ((dataword & 0x2000) != 0) value.Append("Discharge OC. ");
																	if ((dataword & 0x1000) != 0) value.Append("Charge OC. ");
																	if ((dataword & 0x800) != 0) value.Append("Tier-2 Discharge OC. ");
																	if ((dataword & 0x400) != 0) value.Append("Tier-2 Charge OC. ");
																	if ((dataword & 0x200) != 0) value.Append("Pack UV. ");
																	if ((dataword & 0x100) != 0) value.Append("Pack OV. ");
																	if ((dataword & 0x80) != 0) value.Append("Cell UV. ");
																	if ((dataword & 0x40) != 0) value.Append("Cell OV. ");
																	if ((dataword & 0x20) != 0) value.Append("PF. ");
																	if ((dataword & 0x10) != 0) value.Append("Host WD. ");
																	if ((dataword & 0x8) != 0) value.Append("AFE WD. ");
																	if ((dataword & 0x4) != 0) value.Append("Discharge OC. ");
																	if ((dataword & 0x2) != 0) value.Append("Charge SC. ");
																	if ((dataword & 0x1) != 0) value.Append("Discharge SC. ");
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x52: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																	value.Append("BQ208X: Reading a byte from dataflash. ");
																}
																else if (Chip.Contains("20Z"))
																{
																	value.Append("BQ20ZXX: PFAlert: "); //decoding, show in DEC format
																	if ((dataword & 0x8000) != 0) value.Append("Fuse Blow. ");
																	if ((dataword & 0x4000) != 0) value.Append("PF VSHUT. ");
																	if ((dataword & 0x2000) != 0) value.Append("Safety UV. ");
																	if ((dataword & 0x1000) != 0) value.Append("Open Thermistor. ");
																	if ((dataword & 0x800) != 0) value.Append("Discharge Safety OC. ");
																	if ((dataword & 0x400) != 0) value.Append("Charge Safty OC. ");
																	if ((dataword & 0x200) != 0) value.Append("Periodic AFE Com Fault. ");
																	if ((dataword & 0x100) != 0) value.Append("Permanent AFE Com Fault. ");
																	if ((dataword & 0x80) != 0) value.Append("Data Flash Fault. ");
																	if ((dataword & 0x40) != 0) value.Append("Discharge-FET-Failure. ");
																	if ((dataword & 0x20) != 0) value.Append("Charge-FET-Failure. ");
																	if ((dataword & 0x10) != 0) value.Append("Cell-Imbalance. ");
																	if ((dataword & 0x8) != 0) value.Append("Discharge Safety OT. ");
																	if ((dataword & 0x4) != 0) value.Append("Charge Safety OT. ");
																	if ((dataword & 0x2) != 0) value.Append("Safety OV. ");
																	if ((dataword & 0x1) != 0) value.Append("External PF Input. ");
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x53: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																}
																else if (Chip.Contains("20Z"))
																{
																	value.Append("BQ20ZXX: PFStatus: "); //decoding, show in DEC format
																	if ((dataword & 0x8000) != 0) value.Append("Fuse Blow. ");
																	if ((dataword & 0x4000) != 0) value.Append("PF VSHUT. ");
																	if ((dataword & 0x2000) != 0) value.Append("Safety UV. ");
																	if ((dataword & 0x1000) != 0) value.Append("Open Thermistor. ");
																	if ((dataword & 0x800) != 0) value.Append("Discharge Safety OC. ");
																	if ((dataword & 0x400) != 0) value.Append("Charge Safty OC. ");
																	if ((dataword & 0x200) != 0) value.Append("Periodic AFE Com Fault. ");
																	if ((dataword & 0x100) != 0) value.Append("Permanent AFE Com Fault. ");
																	if ((dataword & 0x80) != 0) value.Append("Data Flash Fault. ");
																	if ((dataword & 0x40) != 0) value.Append("Discharge-FET-Failure. ");
																	if ((dataword & 0x20) != 0) value.Append("Charge-FET-Failure. ");
																	if ((dataword & 0x10) != 0) value.Append("Cell-Imbalance. ");
																	if ((dataword & 0x8) != 0) value.Append("Discharge Safety OT. ");
																	if ((dataword & 0x4) != 0) value.Append("Charge Safety OT. ");
																	if ((dataword & 0x2) != 0) value.Append("Safety OV. ");
																	if ((dataword & 0x1) != 0) value.Append("External PF Input. ");
																}
																else
																{
																}
																
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x54: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																}
																else if (Chip.Contains("20Z"))
																{
																	value.Append("BQ20ZXX: OperationStatus: "); //decoding, show in DEC format
																	if ((dataword & 0x8000) != 0) value.Append("System Present. ");
																	if ((dataword & 0x4000) == 0) value.Append("FULL ACCESS Mode. ");
																	if ((dataword & 0x2000) != 0) value.Append("SEALED Mode. ");
																	if ((dataword & 0x1000) != 0) value.Append("DataFlash CheckSum Value Generated. ");
																	if ((dataword & 0x800) != 0) value.Append("Bit11. ");
																	if ((dataword & 0x400) != 0) value.Append("Load Mode. ");
																	if ((dataword & 0x200) != 0) value.Append("Bit09. ");
																	if ((dataword & 0x100) != 0) value.Append("Bit08. ");
																	if ((dataword & 0x80) != 0) value.Append("WAKE Mode. ");
																	if ((dataword & 0x40) != 0) value.Append("Discharging or Relaxation Mode. ");
																	if ((dataword & 0x20) != 0) value.Append("Discharge Fault. ");
																	if ((dataword & 0x10) != 0) value.Append("Discharge Disabled for Current Issue. ");
																	if ((dataword & 0x8) != 0) value.Append("Discharge Inhibited for High Temp. ");
																	if ((dataword & 0x4) != 0) value.Append("Resistance Update Disabled. ");
																	if ((dataword & 0x2) != 0) value.Append("VOK for Qmax Update. ");
																	if ((dataword & 0x1) != 0) value.Append("Qmax Update Enabled. ");
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x55: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																}
																else if (Chip.Contains("20Z"))
																{
																	value.Append("BQ20ZXX: ChargingStatus: "); //decoding, show in DEC format
																	if ((dataword & 0x8000) != 0) value.Append("Charging disabled. ");
																	if ((dataword & 0x4000) == 0) value.Append("Charging suspended. ");
																	if ((dataword & 0x2000) != 0) value.Append("Precharging. ");
																	if ((dataword & 0x1000) != 0) value.Append("Maintenance charging. ");
																	if ((dataword & 0x800) != 0) value.Append("Bit11. ");
																	if ((dataword & 0x400) != 0) value.Append("Bit10. ");
																	if ((dataword & 0x200) != 0) value.Append("Bit09. ");
																	if ((dataword & 0x100) != 0) value.Append("Bit08. ");
																	if ((dataword & 0x80) != 0) value.Append("Bit07. ");
																	if ((dataword & 0x40) != 0) value.Append("Cell balancing. ");
																	if ((dataword & 0x20) != 0) value.Append("Discharge Fault. ");
																	if ((dataword & 0x10) != 0) value.Append("Bit04. ");
																	if ((dataword & 0x8) != 0) value.Append("Bit03. ");
																	if ((dataword & 0x4) != 0) value.Append("Bit02. ");
																	if ((dataword & 0x2) != 0) value.Append("Overcharge fault. ");
																	if ((dataword & 0x1) != 0) value.Append("Bit00. ");
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x57: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																}
																else if (Chip.Contains("20Z"))
																{
																	value.Append("BQ20ZXX: ResetData: Full Resets: " + data[0] + " Partial Resets: " + data[1]); //decoding, show in DEC format
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x58: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																	value.Append("BQ208X: " + data[1] + " bytes Reads/Writes data flash");
																}
																else if (Chip.Contains("20Z"))
																{
																	value.Append("BQ20ZXX: WDResetData: Watchdog resets :" + dataword + "times"); //decoding, show in DEC format
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x59:
															goto case 0x5B;
														case 0x5A: //HEX word
															goto case 0x5B;
														case 0x5B:
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																	value.Append("BQ208X: " + data[1] + " bytes Reads/Writes data flash");
																}
																else if (Chip.Contains("20Z"))
																{
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x5C:
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("2084"))
																{
																	value.Append("BQ2084: " + data[1] + " bytes Reads/Writes data flash");
																}
																else if (Chip.Contains("20Z"))
																{
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x5D: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																}
																else if (Chip.Contains("20Z"))
																{
																	value.Append("BQ20ZXX: AverageVoltage: " + dataword.ToString() + " mV"); //decoding, show in DEC format
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x5E: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																}
																else if (Chip.Contains("20Z4") || Chip.Contains("20Z6"))
																{
																	value.Append("BQ20Z4X/6X: TS1Temperature: " + ((short)dataword).ToString() + " °C"); //decoding, show in DEC format
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x5F: //HEX word
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																}
																else if (Chip.Contains("20Z4") || Chip.Contains("20Z6"))
																{
																	value.Append("BQ20Z4X/6X: TS2Temperature: " + ((short)dataword).ToString() + " °C"); //decoding, show in DEC format
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x60: //HEX string
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																	value.Append("BQ208X: " + data[1] + " bytes Reads/Writes data flash");
																}
																else if (Chip.Contains("20Z"))
																{
																	value.Append("BQ20ZXX: UnSealKey: Read " + data[1] + "-byte-block manually. "); //decoding, show in DEC format
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x61: //HEX string
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																	value.Append("BQ208X: " + data[1] + " bytes Reads/Writes data flash");
																}
																else if (Chip.Contains("20Z"))
																{
																	value.Append("BQ20ZXX: FullAccessKey: Read " + data[1] + "-byte-block manually. "); //decoding, show in DEC format
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x62: //HEX string
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																	value.Append("BQ208X: " + data[1] + " bytes Reads/Writes data flash");
																}
																else if (Chip.Contains("20Z"))
																{
																	value.Append("BQ20ZXX: PFKey: Read " + data[1] + "-byte-block manually. "); //decoding, show in DEC format
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x63: //HEX string
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																	value.Append("BQ208X: " + data[1] + " bytes Reads/Writes data flash");
																}
																else if (Chip.Contains("20Z"))
																{
																	value.Append("BQ20ZXX: AuthenKey3: Read " + data[1] + "-byte-block manually. "); //decoding, show in DEC format
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x64: //HEX string
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																	value.Append("BQ208X: " + data[1] + " bytes Reads/Writes data flash");
																}
																else if (Chip.Contains("20Z"))
																{
																	value.Append("BQ20ZXX: AuthenKey2: Read " + data[1] + "-byte-block manually. "); //decoding, show in DEC format
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x65: //HEX string
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																	value.Append("BQ208X: " + data[1] + " bytes Reads/Writes data flash");
																}
																else if (Chip.Contains("20Z"))
																{
																	value.Append("BQ20ZXX: AuthenKey1: Read " + data[1] + "-byte-block manually. "); //decoding, show in DEC format
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x66: //HEX string
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																	value.Append("BQ208X: " + data[1] + " bytes Reads/Writes data flash");
																}
																else if (Chip.Contains("20Z"))
																{
																	value.Append("BQ20ZXX: AuthenKey0: Read " + data[1] + "-byte-block manually. "); //decoding, show in DEC format
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x67: //HEX string
															goto case 0x6E;
														case 0x68: //HEX string
															goto case 0x6E;
														case 0x69: //HEX string
															goto case 0x6E;
														case 0x6A: //HEX string
															goto case 0x6E;
														case 0x6B: //HEX string
															goto case 0x6E;
														case 0x6C: //HEX string
															goto case 0x6E;
														case 0x6D: //HEX string
															goto case 0x6E;
														case 0x6E: //HEX string
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																	value.Append("BQ208X: " + data[1] + " bytes Reads/Writes data flash");
																}
																else if (Chip.Contains("20Z"))
																{
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x6F: //HEX string
															goto case 0x73;
														case 0x70: //HEX string
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("2084"))
																{
																	value.Append("BQ2084: " + data[1] + " bytes Reads/Writes data flash");
																}
																else if (Chip.Contains("20Z"))
																{
																	value.Append("BQ20ZXX: ManufacturerInfo: Read " + data[1] + "-byte-block manually. "); //decoding, show in DEC format
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x71: //HEX string
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("2084"))
																{
																	value.Append("BQ2084: " + data[1] + " bytes Reads/Writes data flash");
																}
																else if (Chip.Contains("20Z"))
																{
																	value.Append("BQ20ZXX: SenseResistor: " + dataword.ToString() + " µΩ"); //decoding, show in DEC format
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x72: //HEX string
														goto case 0x73;
														case 0x73: //HEX string
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("2084"))
																{
																	value.Append("BQ2084: " + data[1] + " bytes Reads/Writes data flash");
																}
																else if (Chip.Contains("20Z"))
																{
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x77: //HEX string
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																}
																else if (Chip.Contains("20Z"))
																{
																	value.Append("BQ20ZXX: DataFlashSubClassID: Read " + data[1] + "-byte-block manually. "); //decoding, show in DEC format
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x78: //HEX string
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																}
																else if (Chip.Contains("20Z"))
																{
																	value.Append("BQ20ZXX: DataFlashSubClassPage1: Read " + data[1] + "-byte-block manually. "); //decoding, show in DEC format
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x79: //HEX string
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																}
																else if (Chip.Contains("20Z"))
																{
																	value.Append("BQ20ZXX: DataFlashSubClassPage2: Read " + data[1] + "-byte-block manually. "); //decoding, show in DEC format
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x7A: //HEX string
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																}
																else if (Chip.Contains("20Z"))
																{
																	value.Append("BQ20ZXX: DataFlashSubClassPage3: Read " + data[1] + "-byte-block manually. "); //decoding, show in DEC format
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x7B: //HEX string
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																}
																else if (Chip.Contains("20Z"))
																{
																	value.Append("BQ20ZXX: DataFlashSubClassPage4: Read " + data[1] + "-byte-block manually. "); //decoding, show in DEC format
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x7C: //HEX string
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																}
																else if (Chip.Contains("20Z"))
																{
																	value.Append("BQ20ZXX: DataFlashSubClassPage5: Read " + data[1] + "-byte-block manually. "); //decoding, show in DEC format
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x7D: //HEX string
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																}
																else if (Chip.Contains("20Z"))
																{
																	value.Append("BQ20ZXX: DataFlashSubClassPage6: Read " + data[1] + "-byte-block manually. "); //decoding, show in DEC format
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x7E: //HEX string
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																}
																else if (Chip.Contains("20Z"))
																{
																	value.Append("BQ20ZXX: DataFlashSubClassPage7: Read " + data[1] + "-byte-block manually. "); //decoding, show in DEC format
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														case 0x7F: //HEX string
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																if (Chip.Contains("208"))
																{
																}
																else if (Chip.Contains("20Z"))
																{
																	value.Append("BQ20ZXX: DataFlashSubClassPage8: Read " + data[1] + "-byte-block manually. "); //decoding, show in DEC format
																}
																else
																{
																}
																current_byte += 3; //move index to next reg
															}
															break;
														default:
															if(current_reg == Payload[current_byte])
															{
																Array.Copy(Payload, current_byte+1, data, 0, 2); //get register data
																dataword = (ushort)(data[0]<<8 | data[1]); //turn to a word
																value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, 2) + " // "); //show the word in HEX format
																value.Append("Ext_CMD: " + "HEX_" + Convert.ToString(dataword, 16).PadLeft(4, '0') + " BIN_" + Convert.ToString(data[0], 2).PadLeft(8,'0') + '_' + Convert.ToString(data[1], 2).PadLeft(8,'0')  + " DEC_" + dataword + " Or a " + data[1] + "-byte block. "); //decoding, show in BIN HEX DEC format
																current_byte += 3; //move index to next reg
															}
															break;

													}
												if (current_reg != Payload[1]) value.Append(Environment.NewLine);
												if (current_reg == 0xFF) break; // (0xff)++ will be 0x00 not ending loop
												}
                                                Util.UpdateTextBox(CommunicationTextBox, "[INFO] SMBus register dump details (" + Util.ByteToHexString(Payload, 0, 1) + "-" + Util.ByteToHexString(Payload, 1, 2) + "):" + Environment.NewLine + value.ToString(), null);
                                            }
                                            break;
                                        default:
                                            Util.UpdateTextBox(CommunicationTextBox, "[RX->] Data received", Packet);
                                            break;
                                    }
                                    break;
                                case 0x03: // settings
                                    switch (SubDataCode)
                                    {
                                        case 0x01: // current settings
                                        case 0x03:
                                            Util.UpdateTextBox(CommunicationTextBox, "[RX->] Device settings", Packet);
                                            if (Payload.Length > 2)
                                            {
                                                WordByteOrderComboBox.BeginInvoke((MethodInvoker)delegate
                                                {
                                                    WordByteOrderComboBox.SelectedIndex = Payload[0];
                                                });

                                                string reverse = string.Empty;
                                                if ((Payload[0] & 0x03) == 0) reverse = "no reverse";
                                                else if ((Payload[0] & 0x03) == 1) reverse = "reverse read";
                                                else if ((Payload[0] & 0x03) == 2) reverse = "reverse write";
                                                else if ((Payload[0] & 0x03) == 3) reverse = "reverse read/write";
                                                else reverse = "unknown";
                                                Util.UpdateTextBox(CommunicationTextBox, "[INFO] Word byte-order: " + reverse, null);
                                                Util.UpdateTextBox(CommunicationTextBox, "[INFO] Design voltage: " + ((ushort)((Payload[1] << 8) | Payload[2]) / 1000D).ToString("0.0") + " V " + "Design capacity: " + ((ushort)(Payload[3] << 8) | Payload[4]) + " mAH", null);
                                                ChipID = (ushort)((Payload[5] << 8) | Payload[6]);
                                                
                                                switch (ChipID) 
                                                {
                                                		case 0x0823: Chip = "BQ2083"; break;
                                                		case 0x0824: Chip = "BQ2084"; break;
                                                		case 0x0400: Chip = "BQ20Z4X"; break;
                                                		case 0x0600: Chip = "BQ20Z6X"; break;
                                                		case 0x0700: Chip = "BQ20Z7X"; break;
                                                		case 0x0800: Chip = "BQ20Z80"; break;
                                                		case 0x0900: Chip = "BQ20Z9X/30XX"; break;
                                                		default: Chip = "0x" + Convert.ToString(ChipID, 16).PadLeft(4, '0'); break;
                                                }
                                                
                                                Util.UpdateTextBox(CommunicationTextBox, "[INFO] Chip: " + Chip + " Firmware ver " + Convert.ToString(Payload[7],16) + '.' + Convert.ToString(Payload[8], 16), null);
                                            }
                                            break;
                                        case 0x02: // select smbus address
                                            Util.UpdateTextBox(CommunicationTextBox, "[RX->] SMBus settings", Packet);
                                            Util.UpdateTextBox(CommunicationTextBox, "[INFO] Current SMBus device address: " + Util.ByteToHexString(Payload, 0, 1), null);
                                            break;
                                        default:
                                            Util.UpdateTextBox(CommunicationTextBox, "[RX->] Data received", Packet);
                                            break;
                                    }
                                    break;
                                case 0x04: // read data
                                    switch (SubDataCode)
                                    {
                                        case 0x01: // read byte data
                                            Util.UpdateTextBox(CommunicationTextBox, "[RX->] Byte data received", Packet);
                                            if (Payload.Length > 1)
                                            {
                                                string register = Util.ByteToHexString(Payload, 0, 1);
                                                string data = Util.ByteToHexString(Payload, 1, 2);
                                                Util.UpdateTextBox(CommunicationTextBox, "[INFO] Reg.: " + register + Environment.NewLine +
                                                                                            "       Data: " + data, null);

                                                ReadDataTextBox.BeginInvoke((MethodInvoker)delegate
                                                {
                                                    ReadDataTextBox.Text = data;
                                                });
                                            }
                                            break;
                                        case 0x02: // read word data
                                            Util.UpdateTextBox(CommunicationTextBox, "[RX->] Word data received", Packet);
                                            if (Payload.Length > 2)
                                            {
                                                string register = Util.ByteToHexString(Payload, 0, 1);
                                                string data = Util.ByteToHexString(Payload, 1, 3);
                                                Util.UpdateTextBox(CommunicationTextBox, "[INFO] Reg.: " + register + Environment.NewLine +
                                                                                            "       Data: " + data, null);

                                                ReadDataTextBox.BeginInvoke((MethodInvoker)delegate
                                                {
                                                    ReadDataTextBox.Text = data;
                                                });
                                            }
                                            break;
                                        case 0x03: // read block data
                                            Util.UpdateTextBox(CommunicationTextBox, "[RX->] Block data received", Packet);
                                            if (Payload.Length > 2)
                                            {
                                                string register = Util.ByteToHexString(Payload, 0, 1);
                                                string data = Encoding.ASCII.GetString(Payload, 2, Payload.Length - 2);
                                                Util.UpdateTextBox(CommunicationTextBox, "[INFO] Reg.: " + register + Environment.NewLine +
                                                                                            "       Data: " + data, null);

                                                ReadDataTextBox.BeginInvoke((MethodInvoker)delegate
                                                {
                                                    ReadDataTextBox.Text = data;
                                                });
                                            }
                                            break;
                                        case 0x04: // read rom byte
                                            Util.UpdateTextBox(CommunicationTextBox, "[RX->] ROM byte data received", Packet);
                                            if (Payload.Length > 2)
                                            {
                                                string address = Util.ByteToHexString(Payload, 0, 2);
                                                string data = Util.ByteToHexString(Payload, 2, Payload.Length);
                                                Util.UpdateTextBox(CommunicationTextBox, "[INFO] ROM address: " + address + "; Data:" + Environment.NewLine + data, null);

                                                // Save data to a binary file
                                                using (BinaryWriter writer = new BinaryWriter(File.Open(ROMBinaryFilename, FileMode.Append)))
                                                {
                                                    writer.Write(Payload, 2, Payload.Length - 2);
                                                    writer.Close();
                                                }
                                            }
                                            break;
                                        case 0x05: // read rom block
                                            Util.UpdateTextBox(CommunicationTextBox, "[RX->] ROM block data received", Packet);
                                            if (Payload.Length > 2)
                                            {
                                                string address = Util.ByteToHexString(Payload, 0, 2);
                                                string data = Util.ByteToHexString(Payload, 2, Payload.Length);
                                                Util.UpdateTextBox(CommunicationTextBox, "[INFO] ROM address: " + address + "; Data:" + Environment.NewLine + data, null);

                                                // Save data to a binary file
                                                using (BinaryWriter writer = new BinaryWriter(File.Open(ROMBinaryFilename, FileMode.Append)))
                                                {
                                                    writer.Write(Payload, 2, Payload.Length - 2);
                                                    writer.Close();
                                                }
                                            }
                                            break;
                                        default:
                                            Util.UpdateTextBox(CommunicationTextBox, "[RX->] Data received", Packet);
                                            break;
                                    }
                                    break;
                                case 0x05: // write data
                                    switch (SubDataCode)
                                    {
                                        case 0x01: // write data byte
                                            Util.UpdateTextBox(CommunicationTextBox, "[RX->] Byte data write response", Packet);
                                            if (Payload.Length > 2)
                                            {
                                                string register = Util.ByteToHexString(Payload, 0, 1);
                                                string data = Util.ByteToHexString(Payload, 1, 2);
                                                string success = Util.ByteToHexString(Payload, 2, 3);
                                                Util.UpdateTextBox(CommunicationTextBox, "[INFO] Reg.: " + register + Environment.NewLine +
                                                                                            "       Data: " + data + Environment.NewLine +
                                                                                            "       # of bytes written: " + success, null);
                                            }
                                            break;
                                        case 0x02: // write data word
                                            Util.UpdateTextBox(CommunicationTextBox, "[RX->] Word data write response", Packet);
                                            if (Payload.Length > 3)
                                            {
                                                string register = Util.ByteToHexString(Payload, 0, 1);
                                                string data = Util.ByteToHexString(Payload, 1, 3);
                                                string success = Util.ByteToHexString(Payload, 3, 4);
                                                Util.UpdateTextBox(CommunicationTextBox, "[INFO] Reg.: " + register + Environment.NewLine +
                                                                                            "       Data: " + data + Environment.NewLine +
                                                                                            "       # of bytes written: " + success, null);
                                            }
                                            break;
                                        case 0x03: // write data block
                                            Util.UpdateTextBox(CommunicationTextBox, "[RX->] Block data write response", Packet);
                                            if (Payload.Length > 2)
                                            {
                                                string register = Util.ByteToHexString(Payload, 0, 1);
                                                string data = Util.ByteToHexString(Payload, 1, Payload.Length - 1);
                                                string success = Util.ByteToHexString(Payload, Payload.Length - 1, Payload.Length);
                                                Util.UpdateTextBox(CommunicationTextBox, "[INFO] Reg.: " + register + Environment.NewLine +
                                                                                            "       Data: " + data + Environment.NewLine +
                                                                                            "       # of bytes written: " + success, null);
                                            }
                                            break;
                                        default:
                                            Util.UpdateTextBox(CommunicationTextBox, "[RX->] Data received", Packet);
                                            break;
                                    }
                                    break;
                                case 0x0F: // OK/Error
                                    switch (SubDataCode)
                                    {
                                        case 0x00:
                                            Util.UpdateTextBox(CommunicationTextBox, "[RX->] OK", Packet);
                                            break;
                                        case 0x01:
                                            Util.UpdateTextBox(CommunicationTextBox, "[RX->] Error: invalid length", Packet);
                                            break;
                                        case 0x02:
                                            Util.UpdateTextBox(CommunicationTextBox, "[RX->] Error: invalid command", Packet);
                                            break;
                                        case 0x03:
                                            Util.UpdateTextBox(CommunicationTextBox, "[RX->] Error: invalid sub-data code", Packet);
                                            break;
                                        case 0x04:
                                            Util.UpdateTextBox(CommunicationTextBox, "[RX->] Error: invalid payload value(s)", Packet);
                                            break;
                                        case 0x05:
                                            Util.UpdateTextBox(CommunicationTextBox, "[RX->] Error: invalid checksum", Packet);
                                            break;
                                        case 0x06:
                                            Util.UpdateTextBox(CommunicationTextBox, "[RX->] Error: packet timeout occured", Packet);
                                            break;
                                        case 0xFD:
                                            Util.UpdateTextBox(CommunicationTextBox, "[RX->] Error: not enough MCU RAM", Packet);
                                            break;
                                        case 0xFE:
                                            Util.UpdateTextBox(CommunicationTextBox, "[RX->] Error: internal error", Packet);
                                            break;
                                        case 0xFF:
                                            Util.UpdateTextBox(CommunicationTextBox, "[RX->] Error: fatal error", Packet);
                                            break;
                                        default:
                                            Util.UpdateTextBox(CommunicationTextBox, "[RX->] Data received", Packet);
                                            break;
                                    }
                                    break;
                                default:
                                    Util.UpdateTextBox(CommunicationTextBox, "[RX->] Data received", Packet);
                                    break;
                            }
                        }
                    }
                    else
                    {
                        Util.UpdateTextBox(CommunicationTextBox, "[RX->] Data received with checksum error", Packet);
                    }

                    bufferlist.RemoveRange(0, Packet.Length);
                }
                else
                {
                    bufferlist.RemoveAt(0); // remove this byte and see what's next
                }
                break;
            }
        }

        private void SendButton_Click(object sender, EventArgs e)
        {
            if (DeviceFound)
            {
                if (SendComboBox.Text != String.Empty)
                {
                    byte[] bytes = Util.HexStringToByte(SendComboBox.Text);
                    if ((bytes.Length > 4) && (bytes != null))
                    {
                        Util.UpdateTextBox(CommunicationTextBox, "[<-TX] Data transmitted", bytes);
                        Serial.Write(bytes, 0, bytes.Length);

                        if (!SendComboBox.Items.Contains(SendComboBox.Text)) // only add unique items (no repeat!)
                        {
                            SendComboBox.Items.Add(SendComboBox.Text); // add command to the list so it can be selected later
                        }
                    }
                }
            }
        }

        private void SendComboBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Return)
            {
                SendButton.PerformClick();
                e.Handled = true;
            }
        }

        private void ReadRegisterComboBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Return)
            {
                ReadWordButton.PerformClick();
                e.Handled = true;
            }
        }

        private void WriteRegisterComboBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Return)
            {
                WriteWordButton.PerformClick();
                e.Handled = true;
            }
        }

        private void RegStartTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Return)
            {
                SMBusRegisterDumpButton.PerformClick();
                e.Handled = true;
            }
        }

        private void RegEndTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Return)
            {
                SMBusRegisterDumpButton.PerformClick();
                e.Handled = true;
            }
        }

        private void WriteDataTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Return)
            {
                if (Util.HexStringToByte(WriteDataTextBox.Text).Length == 1) WriteByteButton.PerformClick();
                if (Util.HexStringToByte(WriteDataTextBox.Text).Length == 2) WriteWordButton.PerformClick();
                if (Util.HexStringToByte(WriteDataTextBox.Text).Length > 2) WriteBlockButton.PerformClick();
                e.Handled = true;
            }
        }

        private void ResetButton_Click(object sender, EventArgs e)
        {
            byte[] ResetDevice = new byte[] { 0x3D, 0x00, 0x02, 0x00, 0x00, 0x02 };
            Util.UpdateTextBox(CommunicationTextBox, "[<-TX] Reset device", ResetDevice);
            Serial.Write(ResetDevice, 0, ResetDevice.Length);
        }

        private void StatusButton_Click(object sender, EventArgs e)
        {
            byte[] StatusRequest = new byte[] { 0x3D, 0x00, 0x02, 0x02, 0x01, 0x05 }; // timestamp
            Util.UpdateTextBox(CommunicationTextBox, "[<-TX] Status request", StatusRequest);
            Serial.Write(StatusRequest, 0, StatusRequest.Length);
        }

        private void ScanSMBusButton_Click(object sender, EventArgs e)
        {
            byte[] ScanSMBus = new byte[] { 0x3D, 0x00, 0x02, 0x02, 0x02, 0x06 }; // scan SMBus
            Util.UpdateTextBox(CommunicationTextBox, "[<-TX] Scan SMBus", ScanSMBus);
            Serial.Write(ScanSMBus, 0, ScanSMBus.Length);
        }

        private void SMBusAddressSelectButton_Click(object sender, EventArgs e)
        {
            List<byte> packet = new List<byte>();
            byte address = 0;
            bool error = false;

            try
            {
                address = Util.HexStringToByte(SMBusAddressComboBox.Text)[0];
            }
            catch
            {
                error = true;
            }

            if (!error)
            {
                packet.AddRange(new byte[] { 0x3D, 0x00, 0x03, 0x03, 0x02, address });

                byte checksum = 0;
                for (int i = 1; i < packet.Count; i++)
                {
                    checksum += packet[i];
                }
                packet.Add(checksum);

                byte[] SelectSMBusAddress = packet.ToArray();
                Util.UpdateTextBox(CommunicationTextBox, "[<-TX] Select SMBus address", SelectSMBusAddress);
                Serial.Write(SelectSMBusAddress, 0, SelectSMBusAddress.Length);
            }
        }

        private void ReadByteButton_Click(object sender, EventArgs e)
        {
            List<byte> packet = new List<byte>();
            byte reg = 0;
            bool error = false;

            try
            {
                reg = Util.HexStringToByte(ReadRegisterComboBox.Text)[0];
            }
            catch
            {
                error = true;
            }

            if (!error)
            {
                packet.AddRange(new byte[] { 0x3D, 0x00, 0x03, 0x04, 0x01, reg });

                byte checksum = 0;
                for (int i = 1; i < packet.Count; i++)
                {
                    checksum += packet[i];
                }
                packet.Add(checksum);

                byte[] ReadByteData = packet.ToArray();
                Util.UpdateTextBox(CommunicationTextBox, "[<-TX] Read byte data", ReadByteData);
                Serial.Write(ReadByteData, 0, ReadByteData.Length);
            }
        }

        private void ReadWordButton_Click(object sender, EventArgs e)
        {
            List<byte> packet = new List<byte>();
            byte reg = 0;
            bool error = false;

            try
            {
                reg = Util.HexStringToByte(ReadRegisterComboBox.Text)[0];
            }
            catch
            {
                error = true;
            }

            if (!error)
            {
                packet.AddRange(new byte[] { 0x3D, 0x00, 0x03, 0x04, 0x02, reg });

                byte checksum = 0;
                for (int i = 1; i < packet.Count; i++)
                {
                    checksum += packet[i];
                }
                packet.Add(checksum);

                byte[] ReadWordData = packet.ToArray();
                Util.UpdateTextBox(CommunicationTextBox, "[<-TX] Read word data", ReadWordData);
                Serial.Write(ReadWordData, 0, ReadWordData.Length);
            }
        }

        private void ReadBlockButton_Click(object sender, EventArgs e)
        {
            List<byte> packet = new List<byte>();
            byte reg = 0;
            bool error = false;

            try
            {
                reg = Util.HexStringToByte(ReadRegisterComboBox.Text)[0];
            }
            catch
            {
                error = true;
            }

            if (!error)
            {
                packet.AddRange(new byte[] { 0x3D, 0x00, 0x03, 0x04, 0x03, reg });

                byte checksum = 0;
                for (int i = 1; i < packet.Count; i++)
                {
                    checksum += packet[i];
                }
                packet.Add(checksum);

                byte[] ReadBlockData = packet.ToArray();
                Util.UpdateTextBox(CommunicationTextBox, "[<-TX] Read block data", ReadBlockData);
                Serial.Write(ReadBlockData, 0, ReadBlockData.Length);
            }
        }

        private void WriteByteButton_Click(object sender, EventArgs e)
        {
            List<byte> packet = new List<byte>();
            byte reg = 0;
            byte data = 0;
            bool error = false;

            try
            {
                reg = Util.HexStringToByte(WriteRegisterComboBox.Text)[0];
                data = Util.HexStringToByte(WriteDataTextBox.Text)[0];
            }
            catch
            {
                error = true;
            }

            if (!error)
            {
                packet.AddRange(new byte[] { 0x3D, 0x00, 0x04, 0x05, 0x01, reg, data });

                byte checksum = 0;
                for (int i = 1; i < packet.Count; i++)
                {
                    checksum += packet[i];
                }
                packet.Add(checksum);

                byte[] WriteByteData = packet.ToArray();
                Util.UpdateTextBox(CommunicationTextBox, "[<-TX] Write byte data", WriteByteData);
                Serial.Write(WriteByteData, 0, WriteByteData.Length);
            }
        }

        private void WriteWordButton_Click(object sender, EventArgs e)
        {
            List<byte> packet = new List<byte>();
            byte reg = 0;
            byte[] data = new byte[2];
            bool error = false;

            try
            {
                reg = Util.HexStringToByte(WriteRegisterComboBox.Text)[0];
                data = Util.HexStringToByte(WriteDataTextBox.Text);
            }
            catch
            {
                error = true;
            }

            if (!error && (data.Length > 1))
            {
                packet.AddRange(new byte[] { 0x3D, 0x00, 0x05, 0x05, 0x02, reg, data[0], data[1] });

                byte checksum = 0;
                for (int i = 1; i < packet.Count; i++)
                {
                    checksum += packet[i];
                }
                packet.Add(checksum);

                byte[] WriteWordData = packet.ToArray();
                Util.UpdateTextBox(CommunicationTextBox, "[<-TX] Write word data", WriteWordData);
                Serial.Write(WriteWordData, 0, WriteWordData.Length);
            }
        }

        private void WriteBlockButton_Click(object sender, EventArgs e)
        {
            List<byte> packet = new List<byte>();
            byte reg = 0;
            byte[] data = new byte[] { };
            bool error = false;

            try
            {
                reg = Util.HexStringToByte(WriteRegisterComboBox.Text)[0];
                data = Util.HexStringToByte(WriteDataTextBox.Text);
            }
            catch
            {
                error = true;
            }

            if (!error && (data.Length > 0))
            {
                packet.AddRange(new byte[] { 0x3D, (byte)(((3 + data.Length) >> 8) & 0xFF), (byte)((3 + data.Length) & 0xFF), 0x05, 0x03, reg });
                packet.AddRange(data);

                byte checksum = 0;
                for (int i = 1; i < packet.Count; i++)
                {
                    checksum += packet[i];
                }
                packet.Add(checksum);

                byte[] WriteBlockData = packet.ToArray();
                Util.UpdateTextBox(CommunicationTextBox, "[<-TX] Write block data", WriteBlockData);
                Serial.Write(WriteBlockData, 0, WriteBlockData.Length);
            }
        }

        private void SMBusRegisterDumpButton_Click(object sender, EventArgs e)
        {
            List<byte> packet = new List<byte>();
            byte[] reg = new byte[2];
            bool error = false;

            try
            {
                reg[0] = Util.HexStringToByte(RegStartTextBox.Text)[0];
                reg[1] = Util.HexStringToByte(RegEndTextBox.Text)[0];
            }
            catch
            {
                error = true;
            }

            if (!error)
            {
                packet.AddRange(new byte[] { 0x3D, 0x00, 0x04, 0x02, 0x03 });
                packet.AddRange(reg);

                byte checksum = 0;
                for (int i = 1; i < packet.Count; i++)
                {
                    checksum += packet[i];
                }
                packet.Add(checksum);

                byte[] SMBusRegisterDumpRequest = packet.ToArray();
                Util.UpdateTextBox(CommunicationTextBox, "[<-TX] SMBus register dump request", SMBusRegisterDumpRequest);
                Serial.Write(SMBusRegisterDumpRequest, 0, SMBusRegisterDumpRequest.Length);
                SMBusRegisterDumpList.Clear();
            }
        }

        private void AboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            about = new AboutForm(this)
            {
                StartPosition = FormStartPosition.CenterParent
            };
            about.ShowDialog();
        }

        private void WordByteOrderOKButton_Click(object sender, EventArgs e)
        {
            List<byte> packet = new List<byte>();
            packet.AddRange(new byte[] { 0x3D, 0x00, 0x03, 0x03, 0x03, (byte)WordByteOrderComboBox.SelectedIndex });

            byte checksum = 0;
            for (int i = 1; i < packet.Count; i++)
            {
                checksum += packet[i];
            }
            packet.Add(checksum);

            byte[] ChangeWordByteOrder = packet.ToArray();
            Util.UpdateTextBox(CommunicationTextBox, "[<-TX] Change word byte-order settings", ChangeWordByteOrder);
            Serial.Write(ChangeWordByteOrder, 0, ChangeWordByteOrder.Length);
        }

        private void ReadROMButton_Click(object sender, EventArgs e)
        {
            if (ReadROMByBytesCheckBox.Checked)
            {
                byte[] ReadROMByte = new byte[] { 0x3D, 0x00, 0x02, 0x04, 0x04, 0x0A }; // read ROM byte by byte
                Util.UpdateTextBox(CommunicationTextBox, "[<-TX] Read ROM byte by byte", ReadROMByte);
                Serial.Write(ReadROMByte, 0, ReadROMByte.Length);
            }
            else
            {
                byte[] ReadROMBlock = new byte[] { 0x3D, 0x00, 0x02, 0x04, 0x05, 0x0B }; // read ROM block by block
                Util.UpdateTextBox(CommunicationTextBox, "[<-TX] Read ROM block by block", ReadROMBlock);
                Serial.Write(ReadROMBlock, 0, ReadROMBlock.Length);
            }
        }
    }
}
