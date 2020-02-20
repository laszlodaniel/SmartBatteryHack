using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace SmartBatteryHack
{
    public static class Util
    {
        public static bool CompareArrays(byte[] first, byte[] second, int index, int length)
        {
            bool ret = false;

            for (int i = index; i < length; i++)
            {
                if (first[i] == second[i]) ret = true;
                else ret = false;
            }

            return ret;
        }

        public static string ByteToHexString(byte[] data, int index, int length)
        {
            StringBuilder ret = new StringBuilder();
            byte counter = 0;

            if (data != null)
            {
                for (int i = index; i < length; i++)
                {
                    if (length < 17) ret.Append(Convert.ToString(data[i], 16).PadLeft(2, '0').PadRight(3, ' ').ToUpper());
                    else
                    {
                        ret.Append(Convert.ToString(data[i], 16).PadLeft(2, '0').PadRight(3, ' ').ToUpper());

                        counter++;
                        if (counter > 31)
                        {
                            if (i != (length - 1)) ret.Append(Environment.NewLine); // New line after every 16 bytes
                            counter = 0;
                        }
                    }
                }
                //if (counter == 0) ret = ret.Remove(ret.Length - 1, 1); // remove newline character if text is touching the textbox wall
                ret.Replace(" ", String.Empty, ret.Length - 1, 1); // remove whitespace at the end caused by PadRight(3, ' ')
            }
            return ret.ToString();
        }

        public static byte[] HexStringToByte(string str)
        {
            // Remove whitespaces, commas, semi-colons and hex number identifiers
            string ret = str.Trim().Replace(" ", String.Empty).Replace(",", String.Empty).Replace(";", String.Empty).Replace("$", String.Empty).Replace("0x", String.Empty);
            try
            {
                return Enumerable.Range(0, ret.Length).Where(x => x % 2 == 0).Select(x => Convert.ToByte(ret.Substring(x, 2), 16)).ToArray();
            }
            catch
            {
                return new byte[] { }; // return an empty byte array if something is wrong
            }
        }

        public static void UpdateTextBox(TextBox textbox, string text, byte[] bytes)
        {
            string ret = String.Empty;

            if (textbox.Text != "") ret += Environment.NewLine + Environment.NewLine;
            ret += text;
            if (bytes != null) ret += Environment.NewLine + ByteToHexString(bytes, 0, bytes.Length);

            if (textbox.InvokeRequired)
            {
                textbox.BeginInvoke((MethodInvoker)delegate
                {
                    if (textbox.TextLength + ret.Length > textbox.MaxLength)
                    {
                        textbox.Clear();
                        GC.Collect();
                    }
                    textbox.AppendText(ret);
                });
            }
            else
            {
                if (textbox.TextLength + ret.Length > textbox.MaxLength)
                {
                    textbox.Clear();
                    GC.Collect();
                }
                textbox.AppendText(ret);
            }

            // Save text to log-file
            File.AppendAllText(MainForm.USBLogFilename, ret);

            // Save raw USB packet to a binary logfile
            using (BinaryWriter writer = new BinaryWriter(File.Open(MainForm.USBBinaryLogFilename, FileMode.Append)))
            {
                if (bytes != null)
                {
                    writer.Write(bytes);
                    writer.Close();
                }
            }
        }

        public static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
        {
            // Unix timestamp is seconds past epoch
            DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            dtDateTime = dtDateTime.AddSeconds(unixTimeStamp).ToLocalTime();
            return dtDateTime;
        }

        // Chop off input string's end so it becomes maxLength long
        public static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }
    }
}