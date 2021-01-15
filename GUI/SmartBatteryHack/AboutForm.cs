using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace SmartBatteryHack
{
    public partial class AboutForm : Form
    {
        MainForm originalForm;

        public AboutForm(MainForm incomingForm)
        {
            originalForm = incomingForm;
            InitializeComponent();
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            VersionLabel.Text = "GUI v" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
        }

        private void AboutForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            GC.Collect();
        }
    }
}
