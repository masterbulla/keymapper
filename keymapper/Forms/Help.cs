﻿using System.Windows.Forms;
using KeyMapper.Classes;

namespace KeyMapper.Forms
{
    public partial class HelpForm : KMBaseForm
    {
        public HelpForm()
        {
            InitializeComponent();
            LoadUserSettings();
            FormClosed += FormsManager.ChildFormClosed;
            labelFAQ.Links[0].LinkData = "http://justkeepswimming.net/keymapper/faq.aspx";
        }

        private void LoadUserSettings()
        {
            var userSettings = new Properties.Settings();
            chkShowHelpAtStartup.Checked = userSettings.ShowHelpFormAtStartup;
        }

   


        private void HelpFormFormClosed(object sender, FormClosedEventArgs e)
        {
            SaveUserSettings();
        }

        private void SaveUserSettings()
        {
			var userSettings = new Properties.Settings {
				ShowHelpFormAtStartup = chkShowHelpAtStartup.Checked,
				HelpFormLocation = Location
			};

			userSettings.Save();
        }

        private void labelFAQClick(object sender, LinkLabelLinkClickedEventArgs e)
        {
            string url = e.Link.LinkData as string;
            if (string.IsNullOrEmpty(url) == false) {
				System.Diagnostics.Process.Start(url);
			}
		}



    }


}
