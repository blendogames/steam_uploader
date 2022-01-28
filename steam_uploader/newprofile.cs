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
    public partial class NewProfileForm : Form
    {
        public NewProfileForm()
        {
            InitializeComponent();

            this.AcceptButton = button1;
        }

        public void SetInitialValues(string defaultProfiletext, string defaultAppid, string defaultDescription, string caption, string extraCommandArgs)
        {
            this.textBox_profilename.Text = defaultProfiletext;
            this.textBox_appid.Text = defaultAppid;
            this.textBox_desc.Text = defaultDescription;
            this.textBox_extraCommandArgs.Text = extraCommandArgs;

            this.Text = caption;
        }

        public string[] GetCurrentValues()
        {
            return new string[]
            {
                this.textBox_profilename.Text,
                this.textBox_appid.Text,
                this.textBox_desc.Text,
                this.textBox_extraCommandArgs.Text
            };
        }
    }
}
