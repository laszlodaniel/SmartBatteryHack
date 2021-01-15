using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Timers;
using System.Windows.Forms;

namespace SmartBatteryHack
{
    public partial class MainForm : Form
    {
        public string GUIVersion = string.Empty;
        public string DateTimeNow;
        public static string USBLogFilename;
        public static string USBBinaryLogFilename;
        public static string ROMBinaryFilename;
        public bool SerialPortAvailable = false;
        public bool Timeout = false;
        public bool DeviceFound = false;
        public List<byte> bufferlist = new List<byte>();
        public List<ushort> SMBusRegisterDumpList = new List<ushort>();
        public ushort DesignVoltage = 0;
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
            GUIVersion = "v" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            Text += "  |  GUI " + GUIVersion;

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
                                                SMBusRegisterDumpList.Clear();

                                                for (int i = 1; i < (Payload.Length - 2); i++)
                                                {
                                                    i += 2;
                                                    SMBusRegisterDumpList.Add((ushort)((Payload[i] << 8) + Payload[i + 1]));
                                                }

                                                byte[] data = new byte[2];
                                                StringBuilder value = new StringBuilder();
                                                byte start_reg = Payload[0];
                                                byte current_reg = 0;

                                                for (int i = 0; i < SMBusRegisterDumpList.Count; i++)
                                                {
                                                    data[0] = (byte)(SMBusRegisterDumpList[i] >> 8 & 0xFF);
                                                    data[1] = (byte)(SMBusRegisterDumpList[i] & 0xFF);
                                                    current_reg = (byte)(i + start_reg);
                                                    value.Append("[" + Util.ByteToHexString(new byte[] { current_reg }, 0, 1) + "]: " + Util.ByteToHexString(data, 0, data.Length) + " // ");

                                                    switch (current_reg)
                                                    {
                                                        case 0x00:
                                                            value.Append("ManufacturerAccess: " + Util.ByteToHexString(data, 0, data.Length));
                                                            break;
                                                        case 0x01:
                                                            if (DesignVoltage > 0) value.Append("RemainingCapacityAlarm: " + SMBusRegisterDumpList[i].ToString() + " mAh = " + Math.Round((DesignVoltage / 1000D) * SMBusRegisterDumpList[i]).ToString("0") + " mWh");
                                                            else value.Append("RemainingCapacityAlarm: " + SMBusRegisterDumpList[i].ToString() + " mAh");
                                                            break;
                                                        case 0x02:
                                                            value.Append("RemainingTimeAlarm: " + SMBusRegisterDumpList[i].ToString() + " minutes");
                                                            break;
                                                        case 0x03:
                                                            value.Append("BatteryMode: " + Convert.ToString(SMBusRegisterDumpList[i], 2).PadLeft(16, '0'));
                                                            break;
                                                        case 0x04:
                                                            value.Append("AtRate: " + SMBusRegisterDumpList[i].ToString() + " minutes");
                                                            break;
                                                        case 0x05:
                                                            value.Append("AtRateTimeToFull: " + SMBusRegisterDumpList[i].ToString() + " minutes");
                                                            break;
                                                        case 0x06:
                                                            value.Append("AtRateTimeToEmpty: " + SMBusRegisterDumpList[i].ToString() + " minutes");
                                                            break;
                                                        case 0x07:
                                                            string state;
                                                            if (SMBusRegisterDumpList[i] == 0) state = "false";
                                                            else state = "true";
                                                            value.Append("AtRateOK: " + state);
                                                            break;
                                                        case 0x08:
                                                            Double Temperature = Math.Round((SMBusRegisterDumpList[i] - 273.15) / 100, 2);
                                                            value.Append("Temperature: " + Temperature + "°C");
                                                            break;
                                                        case 0x09:
                                                            Double Voltage = SMBusRegisterDumpList[i] / 1000D;
                                                            value.Append("Voltage: " + Voltage + " V");
                                                            break;
                                                        case 0x0A:
                                                            Double Current = SMBusRegisterDumpList[i] / 1000D;
                                                            value.Append("Current: " + Current + " A");
                                                            break;
                                                        case 0x0B:
                                                            Double AverageCurrent = SMBusRegisterDumpList[i] / 1000D;
                                                            value.Append("AverageCurrent: " + AverageCurrent + " A");
                                                            break;
                                                        case 0x0C:
                                                            value.Append("MaxError: " + SMBusRegisterDumpList[i].ToString() + "%");
                                                            break;
                                                        case 0x0D:
                                                            value.Append("RelativeStateOfCharge: " + SMBusRegisterDumpList[i].ToString() + "%");
                                                            break;
                                                        case 0x0E:
                                                            value.Append("AbsoluteStateOfCharge: " + SMBusRegisterDumpList[i].ToString() + "%");
                                                            break;
                                                        case 0x0F:
                                                            value.Append("RemainingCapacity: " + SMBusRegisterDumpList[i].ToString() + " mAh");
                                                            break;
                                                        case 0x10:
                                                            value.Append("FullChargeCapacity: " + SMBusRegisterDumpList[i].ToString() + " mAh");
                                                            break;
                                                        case 0x11:
                                                            value.Append("RunTimeToEmpty: " + SMBusRegisterDumpList[i].ToString() + " minutes");
                                                            break;
                                                        case 0x12:
                                                            value.Append("AverageTimeToEmpty: " + SMBusRegisterDumpList[i].ToString() + " minutes");
                                                            break;
                                                        case 0x13:
                                                            value.Append("AverageTimeToFull: " + SMBusRegisterDumpList[i].ToString() + " minutes");
                                                            break;
                                                        case 0x14:
                                                            Double ChargingCurrent = SMBusRegisterDumpList[i] / 1000D;
                                                            value.Append("ChargingCurrent: " + ChargingCurrent + " A");
                                                            break;
                                                        case 0x15:
                                                            Double ChargingVoltage = SMBusRegisterDumpList[i] / 1000D;
                                                            value.Append("ChargingVoltage: " + ChargingVoltage + " V");
                                                            break;
                                                        case 0x16:
                                                            value.Append("BatteryStatus: " + Convert.ToString(SMBusRegisterDumpList[i], 2).PadLeft(16, '0'));
                                                            break;
                                                        case 0x17:
                                                            value.Append("CycleCount: " + SMBusRegisterDumpList[i].ToString());
                                                            break;
                                                        case 0x18:
                                                            value.Append("DesignCapacity: " + SMBusRegisterDumpList[i].ToString() + " mAh");
                                                            break;
                                                        case 0x19:
                                                            DesignVoltage = SMBusRegisterDumpList[i];
                                                            value.Append("DesignVoltage: " + (DesignVoltage / 1000D).ToString() + " V");
                                                            break;
                                                        case 0x1A:
                                                            value.Append("SpecificationInfo: " + Convert.ToString(SMBusRegisterDumpList[i], 2).PadLeft(16, '0'));
                                                            break;
                                                        case 0x1B:
                                                            int year = 1980 + ((SMBusRegisterDumpList[i] >> 9) & 0x7F);
                                                            int month = (SMBusRegisterDumpList[i] >> 5) & 0x0F;
                                                            int day = SMBusRegisterDumpList[i] & 0x1F;
                                                            DateTime Date = new DateTime(year, month, day);
                                                            value.Append("ManufactureDate: " + Date.ToString("yyyy.MM.dd"));
                                                            break;
                                                        case 0x1C:
                                                            value.Append("SerialNumber: " + Util.ByteToHexString(data, 0, data.Length));
                                                            break;
                                                        case 0x20:
                                                            value.Append("ManufacturerName: " + Util.ByteToHexString(data, 0, data.Length));
                                                            break;
                                                        case 0x21:
                                                            value.Append("DeviceName: " + Util.ByteToHexString(data, 0, data.Length));
                                                            break;
                                                        case 0x22:
                                                            value.Append("DeviceChemistry: " + Util.ByteToHexString(data, 0, data.Length));
                                                            break;
                                                        case 0x23:
                                                            value.Append("ManufacturerData: " + Util.ByteToHexString(data, 0, data.Length));
                                                            break;
                                                        default:
                                                            value.Append(Util.ByteToHexString(data, 0, data.Length));
                                                            break;
                                                    }

                                                    if (i != (SMBusRegisterDumpList.Count - 1)) value.Append(Environment.NewLine);
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
                                                DesignVoltage = (ushort)((Payload[1] << 8) + Payload[2]);
                                                Util.UpdateTextBox(CommunicationTextBox, "[INFO] Word byte-order: " + reverse, null);
                                                Util.UpdateTextBox(CommunicationTextBox, "[INFO] Design voltage: " + (DesignVoltage / 1000D).ToString("0.0") + " V", null);
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
