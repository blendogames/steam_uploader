using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Drawing;
using System.Windows.Forms;

namespace steam_uploader
{

    public static class NewProfilePrompt
    {
        public static string[] ShowDialog(string defaultProfiletext, string defaultAppid, string defaultDescription, string caption, string extraCommandArgs)
        {
            NewProfileForm prompt = new NewProfileForm()
            {
                StartPosition = FormStartPosition.CenterParent
            };

            prompt.SetInitialValues(defaultProfiletext, defaultAppid, defaultDescription, caption, extraCommandArgs);
            prompt.ShowDialog();

            return prompt.GetCurrentValues();
        }
    }




    
}
