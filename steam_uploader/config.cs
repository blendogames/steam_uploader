using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace steam_uploader
{
    public partial class ConfigForm : Form
    {
        public ConfigForm()
        {
            InitializeComponent();

            this.AcceptButton = button1;

            textBox_sdkfolder.Text = Properties.Settings.Default.steamsdk_folder;
            textBox_login.Text = Properties.Settings.Default.steamlogin;
            textBox_password.Text = Properties.Settings.Default.steampassword;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            Properties.Settings.Default.steamsdk_folder = textBox_sdkfolder.Text;
            Properties.Settings.Default.steamlogin = textBox_login.Text;
            Properties.Settings.Default.steampassword = textBox_password.Text;

            Properties.Settings.Default.Save();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            //Clear/forget password.
            textBox_password.Text = string.Empty;
            Properties.Settings.Default.steampassword = string.Empty;
            Properties.Settings.Default.Save();
        }
    }
}
