using System;
using System.Collections.Generic;
using System.ComponentModel;
//using System.Data;
using System.Drawing;
//using System.Linq;
//using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Diagnostics;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

//TODO:

// allow command-line additions.




namespace steam_uploader
{
    public partial class Form1 : Form
    {
        const string PROFILE_FILE = "profiles.json";
        const string PROFILE_BACKUP = "profiles.backup";

        List<ProjectProfile> profiles;
        int lastSelectedIndex = -1;

        BackgroundWorker backgroundWorker; //We do things on background thread so user can still drag window around while build is uploading.
        DateTime start;

        int selectedIndex = -1;

        public Form1()
        {
            InitializeComponent();

            bool foundError = false;

            this.FormClosed += MyClosedHandler;

            
            //check for steamcmd.exe existence.
            if (!string.IsNullOrWhiteSpace(Properties.Settings.Default.steamsdk_folder))
            {
                string steamCmdPath = Path.Combine(Properties.Settings.Default.steamsdk_folder, "builder", "steamcmd.exe");
                if (!File.Exists(steamCmdPath))
                {
                    listBox1.BackColor = Color.Pink;
                    AddLog(string.Format("ERROR: unable to find {0}", steamCmdPath));
                    AddLog("Check the Steam SDK ContentBuilder folder setting in File > Steampipe > Settings");
                    AddLog(string.Empty);
                    foundError = true;
                }
            }

            if (string.IsNullOrWhiteSpace(Properties.Settings.Default.steamsdk_folder) || string.IsNullOrWhiteSpace(Properties.Settings.Default.steamlogin) || string.IsNullOrWhiteSpace(Properties.Settings.Default.steampassword))
            {
                listBox1.BackColor = Color.Pink;
                AddLog("Please set up your Steampipe settings. Go to File > Steampipe > Settings");
                
                if (string.IsNullOrWhiteSpace(Properties.Settings.Default.steamsdk_folder))
                    AddLog("* Missing Steam SDK ContentBuilder folder setting.");
                
                if (string.IsNullOrWhiteSpace(Properties.Settings.Default.steamlogin))
                    AddLog("* Missing Steam login setting.");

                if (string.IsNullOrWhiteSpace(Properties.Settings.Default.steampassword))
                    AddLog("* Missing Steam password setting.");

                AddLog(string.Empty);
                foundError = true;
            }

            //Attempt to load profiles.
            if (!LoadProfiles(out profiles))
            {
                listBox1.BackColor = Color.Pink;
                AddLog("Please set up a project profile. Go to: File > Add new profile");
                AddLog(string.Empty);
                SetButtonsEnabled(false);
                foundError = true;
                return;
            }
            
            if (!foundError)
            {
                AddLog("-- READY TO UPLOAD --");
                AddLog(string.Empty);
            }            

            //Populate the profiles dropdown box.
            for (int i = 0; i < profiles.Count; i++)
            {
                comboBox1.Items.Add(profiles[i].profilename);
            }

            comboBox1.SelectedIndexChanged += new EventHandler(ComboBox1_SelectedIndexChanged);


            //Attempt to load the last-selected profile from previous session.
            if (!string.IsNullOrWhiteSpace(Properties.Settings.Default.lastSelectedProfile))
            {
                int targetIndex = comboBox1.FindStringExact(Properties.Settings.Default.lastSelectedProfile);

                if (targetIndex >= 0)
                    comboBox1.SelectedIndex = targetIndex;
                else
                    comboBox1.SelectedIndex = 0;
            }
            else
            {
                //Default to first profile.
                comboBox1.SelectedIndex = 0;
            }


            dataGridView1.CellValueChanged += new DataGridViewCellEventHandler(dataGridView1_CellValueChanged);
            dataGridView1.LostFocus += new EventHandler(datagrid_LostFocus);
        }

        private void datagrid_LostFocus(object sender, EventArgs e)
        {
            dataGridView1.ClearSelection();
        }

        private void ComboBox1_SelectedIndexChanged(object sender, System.EventArgs e)
        {
            //User has interacted with the profile dropdown box.

            //Gets called even if user chooses same value again. Only update when user actually changes value.
            if (lastSelectedIndex == comboBox1.SelectedIndex)
                return; 

            lastSelectedIndex = comboBox1.SelectedIndex;

            //app id, description.
            toolStripStatusLabel_appid.Text = profiles[comboBox1.SelectedIndex].appid.ToString();
            toolStripStatusLabel_description.Text = profiles[comboBox1.SelectedIndex].description;

            dataGridView1.Rows.Clear();
            dataGridView1.Refresh();

            if (profiles[comboBox1.SelectedIndex].builds == null)
                return;

            if (profiles[comboBox1.SelectedIndex].builds.Length <= 0)
                return;

            for (int i = 0; i < profiles[comboBox1.SelectedIndex].builds.Length; i++)
            {
                //Populate the data grid fields.
                DataGridViewRow row = new DataGridViewRow();
                row.CreateCells(dataGridView1);  // this line was missing
                row.Cells[0].Value = profiles[comboBox1.SelectedIndex].builds[i].depotid;
                row.Cells[1].Value = profiles[comboBox1.SelectedIndex].builds[i].folder;
                dataGridView1.Rows.Add(row);

                if (profiles[comboBox1.SelectedIndex].builds[i].depotid <= 0)
                {
                    AddLog(string.Format("ERROR: {0} is not a valid Depot ID value.", profiles[comboBox1.SelectedIndex].builds[i].depotid));
                }
            }

            
        }

        void dataGridView1_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            //User has interacted with the build list.

            //Commit changes to the profiles array.
            List<ProfileBuilds> buildList = new List<ProfileBuilds>();

            for (int i = 0; i < dataGridView1.Rows.Count; i++)
            {
                if (dataGridView1.Rows[i].Cells[0].Value == null || dataGridView1.Rows[i].Cells[1].Value == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(dataGridView1.Rows[i].Cells[0].Value.ToString()) || string.IsNullOrWhiteSpace(dataGridView1.Rows[i].Cells[1].Value.ToString()))
                {
                    continue;
                }

                ProfileBuilds newBuild = new ProfileBuilds();

                int depotValue = 0;
                if (!int.TryParse(dataGridView1.Rows[i].Cells[0].Value.ToString(), out depotValue))
                {
                    AddLog(string.Format("ERROR: {0} is not a valid Depot ID value.", dataGridView1.Rows[i].Cells[0].Value.ToString()));
                }
                else
                {
                    newBuild.depotid = depotValue;
                }                

                newBuild.folder = dataGridView1.Rows[i].Cells[1].Value.ToString();
                buildList.Add(newBuild);
            }

            profiles[comboBox1.SelectedIndex].builds = buildList.ToArray();
        }

        private bool LoadProfiles(out List<ProjectProfile> output)
        {
            SetButtonsEnabled(false);

            output = new List<ProjectProfile>();

            if (!File.Exists(PROFILE_FILE))
            {
                return false;
            }

            string fileContents = GetFileContents(PROFILE_FILE);

            if (string.IsNullOrWhiteSpace(fileContents))
            {
                return false;
            }

            //Load the profiles json file.
            try
            {
                output = JsonConvert.DeserializeObject<List<ProjectProfile>>(fileContents);
            }
            catch (Exception e)
            {
                AddLog(string.Format("Error: can't parse {0}. Error: {1}", PROFILE_FILE, e.Message));
                return false;
            }

            if (output.Count <= 0)
            {
                return false;
            }

            SetButtonsEnabled(true);
            return true;
        }

        private void SetButtonsEnabled(bool value)
        {
            button1.Enabled = value;
            comboBox1.Enabled = value;
            dataGridView1.Enabled = value;
            dataGridView1.DefaultCellStyle.BackColor = value ? Color.White : Color.DarkGray;

            if (value)
            {
                dataGridView1.CellValueChanged += new DataGridViewCellEventHandler(dataGridView1_CellValueChanged);
                dataGridView1.LostFocus += new EventHandler(datagrid_LostFocus);
            }
        }

        private string GetFileContents(string filepath)
        {
            string output = "";

            try
            {
                using (FileStream stream = File.Open(filepath, FileMode.Open, FileAccess.Read))
                {
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        //dump file contents into a string.
                        output = reader.ReadToEnd();
                    }
                }
            }
            catch (Exception e)
            {
                AddLog(string.Format("Error: can't read \n{0}. Error: {1}", filepath, e.Message));
                listBox1.BackColor = Color.Pink;
                return string.Empty;
            }

            return output;
        }

        protected void MyClosedHandler(object sender, EventArgs e)
        {
            SaveSettings();
        }

        private void SaveSettings()
        {
            //Before saving, make a backup file.
            try
            {
                File.Copy(PROFILE_FILE, PROFILE_BACKUP, true);
            }
            catch (Exception err)
            {
                AddLog(string.Format("Error in making backup file. Error: {0}", err.Message));
            }

            //Save changes to json file.
            try
            {
                using (StreamWriter file = File.CreateText(PROFILE_FILE))
                {
                    //Nice formatting:
                    string output = JsonConvert.SerializeObject(profiles, Formatting.Indented);
                    file.Write(output);
                }

                if (comboBox1.SelectedIndex >= 0)
                {
                    Properties.Settings.Default.lastSelectedProfile = comboBox1.Items[comboBox1.SelectedIndex].ToString();
                    Properties.Settings.Default.Save();
                }
            }
            catch (Exception err)
            {
                MessageBox.Show(string.Format("Error in saving {0} file.\n\nError:\n{1}", PROFILE_FILE, err.Message), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AddLog(string text)
        {
            listBox1.Items.Add(text);

            int nItems = (int)(listBox1.Height / listBox1.ItemHeight);
            listBox1.TopIndex = listBox1.Items.Count - nItems;

            this.Update();
            this.Refresh();
        }

        private void AddLogInvoke(string text)
        {
            MethodInvoker mi = delegate() { AddLog(text); };
            this.Invoke(mi);
        }

        private void addNewProfileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            listBox1.BackColor = Color.White;

            //Prompt for profile name.
            string[] promptValues = NewProfilePrompt.ShowDialog(string.Empty, string.Empty, string.Empty, "Add new profile");

            if (string.IsNullOrWhiteSpace(promptValues[0]))
            {
                AddLog("Error: can't use empty profile name. Cancelled.");
                return;
            }

            //Check if it already exists.
            for (int i = 0; i < comboBox1.Items.Count; i++)
            {
                string comboboxText = (comboBox1.Items[i]).ToString();

                if (string.Compare(comboboxText, promptValues[0], StringComparison.InvariantCultureIgnoreCase) == 0)
                {
                    listBox1.BackColor = Color.Pink;
                    AddLog(string.Format("Error: profile '{0}' already exists.", promptValues[0]));
                    return;
                }
            }

            ProjectProfile newProfile = new ProjectProfile();
            newProfile.profilename = promptValues[0];

            int appIDvalue = 0;
            if (int.TryParse(promptValues[1], out appIDvalue))
            {
                toolStripStatusLabel_appid.Text = promptValues[1];
                newProfile.appid = appIDvalue;
            }
            else
            {
                AddLog(string.Format("ERROR: ignoring invalid appid '{0}'", promptValues[1]));
            }

            toolStripStatusLabel_description.Text = promptValues[2];
            newProfile.description = promptValues[2];

            //newProfile.description
            profiles.Add(newProfile);

            comboBox1.Items.Add(promptValues[0]);
            comboBox1.SelectedIndex = comboBox1.FindStringExact(promptValues[0]);

            AddLog(string.Empty);
            AddLog(string.Format("Added new profile: {0}", promptValues[0]));
            listBox1.BackColor = Color.White;
            SetButtonsEnabled(true);            
            
        }

        private void saveChangesExitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void deleteThisProfileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            listBox1.BackColor = Color.White;

            if (comboBox1.SelectedIndex < 0)
            {
                listBox1.BackColor = Color.Pink;
                AddLog("Error: can't delete, no profile selected.");
                return;
            }

            string profilename = (comboBox1.Items[comboBox1.SelectedIndex]).ToString();
            profiles.RemoveAt(comboBox1.SelectedIndex);
            comboBox1.Items.RemoveAt(comboBox1.SelectedIndex);


            if (comboBox1.Items.Count > 0)
                comboBox1.SelectedIndex = 0;
            else
            {
                //No profile to select.

                //Clear out the UI.
                toolStripStatusLabel_appid.Text = "-";
                toolStripStatusLabel_description.Text = "-";

                dataGridView1.Rows.Clear();
                dataGridView1.Refresh();
            }

            AddLog(string.Format("Deleted profile: {0}", profilename));

            if (profiles.Count <= 0)
            {
                SetButtonsEnabled(false);
            }
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show("Blendo steam uploader\nby Brendon Chung\n\nUse this program to upload projects to Steam. This is a graphical GUI wrapper around Steam's Steampipe command-line tools.", "About", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void configureToolStripMenuItem_Click(object sender, EventArgs e)
        {
            
        }

        private void configureThisProfileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //Modify existing profile.

            listBox1.BackColor = Color.White;

            if (comboBox1.SelectedIndex < 0)
            {
                listBox1.BackColor = Color.Pink;
                AddLog("Error: no profile selected.");
                return;
            }

            string curProfilename = profiles[comboBox1.SelectedIndex].profilename;
            string curAppid = profiles[comboBox1.SelectedIndex].appid.ToString();
            string curDesc = profiles[comboBox1.SelectedIndex].description;

            

            string[] promptValues = NewProfilePrompt.ShowDialog(curProfilename, curAppid, curDesc, "Modify current profile");

            bool modified = ((curProfilename != promptValues[0])
                || (curAppid.ToString() != promptValues[1])
                || (curDesc != promptValues[2]));

            //Set appid.
            int appIDvalue = 0;
            if (int.TryParse(promptValues[1], out appIDvalue))
            {
                toolStripStatusLabel_appid.Text = promptValues[1];
                profiles[comboBox1.SelectedIndex].appid = appIDvalue;
            }
            else
            {
                AddLog(string.Format("ERROR: ignoring invalid appid '{0}'", promptValues[1]));
            }

            //Set description.
            toolStripStatusLabel_description.Text = promptValues[2];
            profiles[comboBox1.SelectedIndex].description = promptValues[2];


            //Set profilename. Check if it already exists.
            if (string.IsNullOrWhiteSpace(promptValues[0]))
            {
                AddLog("Error: profile name can't be empty.");
                return;
            }

            for (int i = 0; i < comboBox1.Items.Count; i++)
            {
                string comboboxText = (comboBox1.Items[i]).ToString();

                if (string.Compare(comboboxText, promptValues[0], StringComparison.InvariantCultureIgnoreCase) == 0 && comboBox1.SelectedIndex != i)
                {
                    listBox1.BackColor = Color.Pink;
                    AddLog(string.Format("Error: profile '{0}' already exists.", promptValues[0]));
                    return;
                }
            }

            profiles[comboBox1.SelectedIndex].profilename = promptValues[0];
            comboBox1.Items[comboBox1.SelectedIndex] = promptValues[0];

            if (modified)
                AddLog("Profile settings have been updated.");
        }

        private void openProfileFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            listBox1.BackColor = Color.White;

            try
            {
                Process.Start(PROFILE_FILE);
            }
            catch (Exception err)
            {
                AddLog(string.Format("Error: {0}", err.Message));
                listBox1.BackColor = Color.Pink;
            }
        }

        private void copyAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            listBox1.BackColor = Color.White;

            string output = string.Empty;

            foreach (object item in listBox1.Items)
                output += item.ToString() + "\r\n";

            if (string.IsNullOrWhiteSpace(output))
            {
                AddLog(string.Empty);
                AddLog("No log found.");
                return;
            }

            Clipboard.SetText(output);

            AddLog(string.Empty);
            AddLog("Copied entire log to clipboard.");
        }

        private void copySelectionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            listBox1.BackColor = Color.White;

            string output = string.Empty;

            foreach (object item in listBox1.SelectedItems)
            {
                output += item.ToString() + "\r\n";
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                AddLog(string.Empty);
                AddLog("No selected log found.");
                return;
            }

            Clipboard.SetText(output);

            AddLog(string.Empty);
            AddLog("Copied selected log to clipboard.");
        }

        private void clearToolStripMenuItem_Click(object sender, EventArgs e)
        {
            listBox1.Items.Clear();
            listBox1.BackColor = Color.White;
        }

        private void statusStrip1_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            configureThisProfileToolStripMenuItem_Click(null, null);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            //Click the upload button.
            listBox1.BackColor = Color.White;
            AddLog("-- STARTING UPLOAD --");
            AddLog(string.Empty);
            
            dataGridView1.ClearSelection();

            //Do sanity checks.
            if (!SanityCheck())
            {
                return;
            }

            selectedIndex = comboBox1.SelectedIndex;

            if (generateVDFFilesToolStripMenuItem.Checked)
            {
                //Now generate the VDF files.
                if (!GenerateVDFfiles())
                {
                    listBox1.BackColor = Color.Pink;
                    AddLog("ERROR: failed to generate VDF files.");
                    return;
                }

                AddLog(string.Empty);
            }
            else
            {
                AddLog("Skipping VDF file generation...");
            }

            if (uploadTheBuildToolStripMenuItem.Checked)
            {
                //Settings look good. Let's start uploading.
                SetButtonsEnabled(false);
                start = DateTime.Now;

                backgroundWorker = new BackgroundWorker();
                backgroundWorker.DoWork += OnUploadDoWork;
                backgroundWorker.RunWorkerCompleted += OnUploadCompleted;
                backgroundWorker.RunWorkerAsync();
            }
            else
            {
                AddLog(string.Empty);
                AddLog("Skipping build upload...");
                AddLog(string.Empty);
                AddLog("-- DONE --");
            }
        }

        private void OnUploadDoWork(object sender, DoWorkEventArgs e)
        {
            e.Result = true;

            string appFilename = GetAppFileName();

            if (string.IsNullOrEmpty(appFilename))
            {
                AddLogInvoke("ERROR: failed to find VDF file.");
                e.Result = false;
                return;
            }

            string pathToVDFfile = Path.Combine(Properties.Settings.Default.steamsdk_folder, "scripts", appFilename);
            string steamcmdPath = Path.Combine(Properties.Settings.Default.steamsdk_folder, "builder", "steamcmd.exe");
            string arguments = string.Format("+login {0} {1} +run_app_build \"{2}\" +quit", Properties.Settings.Default.steamlogin, Properties.Settings.Default.steampassword, pathToVDFfile);

            string displayArguments = string.Format("+run_app_build \"{0}\" +quit", pathToVDFfile);
            AddLogInvoke(displayArguments);
            AddLogInvoke(string.Empty);
            AddLogInvoke("Please wait...");
            AddLogInvoke(string.Empty);


            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = steamcmdPath;
            startInfo.Arguments = arguments;
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.CreateNoWindow = true;
            Process proc = new Process();

            bool hasError = false;

            try
            {
                proc.StartInfo = startInfo;
                proc.Start();

                while (!proc.StandardOutput.EndOfStream)
                {
                    string line = proc.StandardOutput.ReadLine();
                    AddLogInvoke("    " + line);

                    if (!hasError)
                    {
                        if (line.IndexOf("]: ERROR! ", StringComparison.InvariantCultureIgnoreCase) >= 0) //Check output for errors.
                        {
                            hasError = true;
                        }
                        else if (line.IndexOf("failed login with result code", StringComparison.InvariantCultureIgnoreCase) >= 0) //Check output for errors.
                        {
                            hasError = true;
                        }
                    }
                }
            }
            catch (Exception err)
            {
                AddLogInvoke(string.Format("Error: {0}", err));
                e.Result = false;
                return;
            }

            if (hasError)
            {
                e.Result = false;
                return;
            }

            e.Result = true;
        }

        private void OnUploadCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                AddLog(string.Empty);
                AddLog(string.Format("ERROR: {0}", e.Error));
                listBox1.BackColor = Color.Pink;
                SetButtonsEnabled(true);
                return;
            }

            if (e.Result is bool)
            {
                bool returnvalue = (bool)e.Result;

                if (!returnvalue)
                {
                    //Error.
                    listBox1.BackColor = Color.Pink;
                    AddLog(string.Empty);
                    AddLog("ERROR: upload failed.");
                    SetButtonsEnabled(true);
                    return;
                }
            }

            SetButtonsEnabled(true);

            TimeSpan delta = DateTime.Now.Subtract(start);
            AddLog(string.Empty);

            string timeStr = string.Empty;
            if (delta.TotalSeconds >= 60)
                timeStr = (string.Format("{0} minutes", Math.Round(delta.TotalMinutes, 1)));
            else
                timeStr = (string.Format("{0} seconds", Math.Round(delta.TotalSeconds, 1)));

            AddLog(string.Format("-- DONE -- (deployment time: {0})", timeStr));
            AddLog(string.Empty);
            listBox1.BackColor = Color.GreenYellow;
        }

        //Return FALSE if anything is bad.
        private bool SanityCheck()
        {
            List<string> errorList = new List<string>();

            if (comboBox1.SelectedIndex < 0)
            {
                errorList.Add("* No profile selected.");
                errorList.Add("  Solution: go to File > Add new profile");
                return false;
            }

            //Check contentbuilder folder.
            if (string.IsNullOrWhiteSpace(Properties.Settings.Default.steamsdk_folder))
            {
                errorList.Add("* Steam SDK ContentBuilder folder has not been set.");
                errorList.Add("  Solution: go to File > Steampipe > Settings");
            }
            else if (!Directory.Exists(Properties.Settings.Default.steamsdk_folder))
            {
                errorList.Add(string.Format("* Unable to find Steam SDK ContentBuilder folder: {0}", Properties.Settings.Default.steamsdk_folder));
                errorList.Add("  Solution: go to File > Steampipe > Settings");
            }
            else
            {
                //check if steamcmd.exe exists.
                string steamcmdPath = Path.Combine(Properties.Settings.Default.steamsdk_folder, "builder", "steamcmd.exe");
                FileInfo steamcmdFile = new FileInfo(steamcmdPath);

                if (!steamcmdFile.Exists)
                {
                    errorList.Add(string.Format("* Unable to find: ", steamcmdPath));
                    errorList.Add("  Solution: go to File > Steampipe > Settings");
                }
            }


            if (string.IsNullOrWhiteSpace(Properties.Settings.Default.steamlogin))
            {
                errorList.Add("* Missing Steam login setting.");
                errorList.Add("  Solution: go to File > Steampipe > Settings");
            }

            if (string.IsNullOrWhiteSpace(Properties.Settings.Default.steampassword))
            {
                errorList.Add("* Missing Steam password setting.");
                errorList.Add("  Solution: go to File > Steampipe > Settings");
            }

            //Check appid number.
            if (profiles[comboBox1.SelectedIndex].appid <= 0)
            {
                errorList.Add(string.Format("* Profile has invalid AppID ({0}).", profiles[comboBox1.SelectedIndex].appid));
                errorList.Add("  Solution: go to File > Profile management > Modify current profile");
            }

            //Check the depot id numbers.
            for (int i = 0; i < dataGridView1.Rows.Count - 1; i++)
            {
                if (dataGridView1.Rows[i].Cells[0].Value == null)
                {
                    errorList.Add(string.Format("* Missing Depot ID value in row {0}.", (i + 1)));
                    errorList.Add(string.Format("  Solution: add Depot ID to row {0}.", (i+1)));
                }
                else if (string.IsNullOrWhiteSpace(dataGridView1.Rows[i].Cells[0].Value.ToString()))
                {
                    errorList.Add(string.Format("* Missing Depot ID value in row {0}.", (i + 1)));
                    errorList.Add(string.Format("  Solution: add Depot ID to row {0}.", (i + 1)));
                }
                else
                {
                    int outDepotid = 0;
                    if (!int.TryParse(dataGridView1.Rows[i].Cells[0].Value.ToString(), out outDepotid))
                    {
                        errorList.Add(string.Format("* Invalid Depot ID value in row {0}: {1}", (i + 1), dataGridView1.Rows[i].Cells[0].Value.ToString()));
                        errorList.Add(string.Format("  Solution: add valid Depot ID to row {0}.", (i + 1)));
                    }
                }
            }

            //Check the depot folders.
            for (int i = 0; i < dataGridView1.Rows.Count - 1; i++)
            {
                if (dataGridView1.Rows[i].Cells[1].Value == null)
                {
                    errorList.Add(string.Format("* Missing Local Folder value in row {0}.", (i + 1)));
                    errorList.Add(string.Format("  Solution: add Local Folder to row {0}.", (i + 1)));
                }
                else if (string.IsNullOrWhiteSpace(dataGridView1.Rows[i].Cells[1].Value.ToString()))
                {
                    errorList.Add(string.Format("* Missing Local Folder value in row {0}.", (i + 1)));
                    errorList.Add(string.Format("  Solution: add Local Folder to row {0}.", (i + 1)));
                }
                else
                {
                    if (!Directory.Exists(dataGridView1.Rows[i].Cells[1].Value.ToString()))
                    {
                        errorList.Add(string.Format("* Unable to find Local Folder in row {0}: {1}", (i + 1), dataGridView1.Rows[i].Cells[1].Value.ToString()));
                        errorList.Add(string.Format("  Solution: verify existence of folder: {0}", dataGridView1.Rows[i].Cells[1].Value.ToString()));
                    }
                }
            }          
            
            //Summary.
            if (errorList.Count > 0)
            {
                listBox1.BackColor = Color.Pink;
                AddLog("ERRORS FOUND:");
                AddLog(string.Empty);
                for (int i = 0; i < errorList.Count; i++)
                {
                    AddLog(errorList[i]);

                    if (!errorList[i].StartsWith("*"))
                    {
                        AddLog(string.Empty);
                    }
                }

                AddLog("Cancelling upload.");

                return false;
            }

            return true;
        }


        private bool GenerateVDFfiles()
        {
            //Brute-force write out the app VDF file.

            if (!GenerateAppFile())
            {
                return false;
            }

            if (!GenerateDepotFiles())
            {
                return false;
            }

            return true;
        }

        private bool GenerateAppFile()
        {
            string app_vdf_contents = "\"appbuild\"\n";
            app_vdf_contents += "{\n";
            app_vdf_contents += string.Format("    \"appid\" \"{0}\"\n", profiles[comboBox1.SelectedIndex].appid);
            app_vdf_contents += string.Format("    \"desc\" \"{0}\"\n", profiles[comboBox1.SelectedIndex].description);
            app_vdf_contents += string.Format("    \"buildoutput\" \"{0}\"\n", Path.Combine(Properties.Settings.Default.steamsdk_folder, "output"));
            app_vdf_contents += "    \"contentroot\" \"\"\n";
            app_vdf_contents += "    \"setlive\" \"\"\n";
            app_vdf_contents += "    \"preview\" \"0\"\n";
            app_vdf_contents += "    \"local\" \"\"\n";
            app_vdf_contents += "    \"depots\"\n";
            app_vdf_contents += "    {\n";

            for (int i = 0; i < dataGridView1.Rows.Count - 1; i++)
            {
                string depotID = dataGridView1.Rows[i].Cells[0].Value.ToString();
                string depotVDFPath = Path.Combine(Properties.Settings.Default.steamsdk_folder, "scripts", GetDepotFileName(depotID));
                app_vdf_contents += string.Format("        \"{0}\" \"{1}\"\n", depotID, depotVDFPath);
            }

            app_vdf_contents += "    }\n";
            app_vdf_contents += "}";

            string appFilename = GetAppFileName();

            if (string.IsNullOrEmpty(appFilename))
            {
                AddLog("ERROR: failed to generate app VDF filename.");
                return false;
            }

            string appPath = Path.Combine(Properties.Settings.Default.steamsdk_folder, "scripts", appFilename);
            if (!WriteToTextfile(appPath, app_vdf_contents))
            {
                AddLog("ERROR: failed to write app VDF file.");
                return false;
            }

            AddLog(string.Format("Generated: {0}", appPath));

            return true;
        }

        private bool GenerateDepotFiles()
        {
            for (int i = 0; i < dataGridView1.Rows.Count - 1; i++)
            {
                string depotID = dataGridView1.Rows[i].Cells[0].Value.ToString();
                string contentFolder = dataGridView1.Rows[i].Cells[1].Value.ToString();

                string depot_vdf_contents = "\"DepotBuildConfig\"\n";
                depot_vdf_contents += "{\n";
                depot_vdf_contents += string.Format("    \"DepotID\" \"{0}\"\n", depotID);
                depot_vdf_contents += string.Format("    \"contentroot\" \"{0}\"\n", contentFolder);
                depot_vdf_contents += "    \"FileMapping\"\n";
                depot_vdf_contents += "    {\n";
                depot_vdf_contents += "        \"LocalPath\" \"*\"\n";
                depot_vdf_contents += "        \"DepotPath\" \".\"\n";
                depot_vdf_contents += "        \"recursive\" \"1\"\n";
                depot_vdf_contents += "    }\n";
                depot_vdf_contents += "}";

                string filename = GetDepotFileName(depotID);

                if (string.IsNullOrEmpty(filename))
                {
                    AddLog("ERROR: failed to generate depot VDF filename.");
                    return false;
                }

                string depotVDFPath = Path.Combine(Properties.Settings.Default.steamsdk_folder, "scripts", filename);
                if (!WriteToTextfile(depotVDFPath, depot_vdf_contents))
                {
                    AddLog("ERROR: failed to write depot VDF file.");
                    return false;
                }

                AddLog(string.Format("Generated: {0}", depotVDFPath));
            }

            return true;
        }



        private string GetAppFileName()
        {
            string sanitizedDescription = string.Empty;

            try
            {
                char[] invalids = Path.GetInvalidFileNameChars();
                sanitizedDescription = String.Join("_", profiles[selectedIndex].profilename.Split(invalids, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.'); //Remove invalid characters.
                sanitizedDescription = sanitizedDescription.Replace(" ", string.Empty); //Remove spaces.
                sanitizedDescription = sanitizedDescription.ToLowerInvariant(); //Make it lowercase.
            }
            catch (Exception err)
            {
                AddLogInvoke(string.Format("ERROR: failed to generate app filename. {0}", err.Message));
                return string.Empty;
            }

            return string.Format("app_{0}_{1}.vdf", profiles[selectedIndex].appid, sanitizedDescription);
        }

        private string GetDepotFileName(string depotID)
        {
            string sanitizedDescription = string.Empty;

            try
            {
                char[] invalids = Path.GetInvalidFileNameChars();
                sanitizedDescription = String.Join("_", profiles[selectedIndex].profilename.Split(invalids, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.'); //Remove invalid characters.
                sanitizedDescription = sanitizedDescription.Replace(" ", string.Empty); //Remove spaces.
                sanitizedDescription = sanitizedDescription.ToLowerInvariant(); //Make it lowercase.
            }
            catch (Exception err)
            {
                AddLogInvoke(string.Format("ERROR: failed to generate depot filename. {0}", err.Message));
                return string.Empty;
            }

            return string.Format("depot_{0}_{1}.vdf", depotID, sanitizedDescription);
        }


        private bool WriteToTextfile(string filePath, string text)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    using (FileStream fileStream = File.Open(filePath, FileMode.OpenOrCreate, System.IO.FileAccess.Write, FileShare.Read))
                    {
                        using (StreamWriter streamWriter = new StreamWriter(fileStream))
                        {
                            streamWriter.WriteLine(string.Empty);
                        }
                    }
                }

                using (FileStream fileStream = File.Open(filePath, FileMode.Truncate, System.IO.FileAccess.Write, FileShare.Read))
                {
                    using (StreamWriter streamWriter = new StreamWriter(fileStream))
                    {
                        streamWriter.WriteLine(text);
                    }
                }
            }
            catch (Exception e)
            {
                AddLog(string.Format("ERROR: failed to write {0}", filePath));
                return false;
            }

            return true;
        }

        private void configureToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            ConfigForm configform = new ConfigForm();
            configform.ShowDialog();

            listBox1.BackColor = Color.White;
            AddLog("Steampipe settings updated.");
        }

        private void viewBuildLogsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ViewFolder("output");
        }

        private void ViewFolder(string subfolder)
        {
            //Open a folder.

            if (string.IsNullOrWhiteSpace(Properties.Settings.Default.steamsdk_folder))
            {
                AddLog("ERROR: no Steam SDK ContentBuilder folder set. Go to File > Steampipe > Settings");
                return;
            }

            if (!Directory.Exists(Properties.Settings.Default.steamsdk_folder))
            {
                AddLog("ERROR: unable to find Steam SDK ContentBuilder folder. Go to File > Steampipe > Settings");
                return;
            }

            string logFolder = Path.Combine(Properties.Settings.Default.steamsdk_folder, subfolder);
            AddLog(string.Format("Opening: {0}", logFolder));
            if (!Directory.Exists(logFolder))
            {
                AddLog("ERROR: unable to open folder.");
                return;
            }

            try
            {
                Process.Start(logFolder);
            }
            catch (Exception err)
            {
                AddLog(string.Format("Error: {0}", err.Message));
                listBox1.BackColor = Color.Pink;
            }
        }

        private void viewVDFFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ViewFolder("scripts");
        }

        private void generateVDFFilesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (generateVDFFilesToolStripMenuItem.Checked)
                AddLog("UPDATED: build process will generate VDF files.");
            else
                AddLog("UPDATED: build process will NOT generate VDF files.");
        }

        private void uploadTheBuildToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (uploadTheBuildToolStripMenuItem.Checked)
                AddLog("UPDATED: build process will upload the build.");
            else
                AddLog("UPDATED: build process will NOT upload the build.");
        }

        private void loginToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Properties.Settings.Default.steamsdk_folder))
            {
                AddLog("ERROR: Steam SDK ContentBuilder folder has not been set. Go to File > Steampipe > Settings");
                return;
            }

            if (!Directory.Exists(Properties.Settings.Default.steamsdk_folder))
            {
                AddLog("ERROR: invalid Steam SDK ContentBuilder folder. Go to File > Steampipe > Settings");
                return;
            }


            //Attempt to run steam cmd
            string exename = Path.Combine(Properties.Settings.Default.steamsdk_folder, "builder", "steamcmd.exe");

            if (!File.Exists(exename))
            {
                AddLog(string.Format("ERROR: cannot find {0}", exename));
                return;
            }

            try
            {
                Process.Start(exename);
            }
            catch (Exception err)
            {
                AddLog(string.Format("Error: {0}", err.Message));
                listBox1.BackColor = Color.Pink;
            }
        }
    }
}

