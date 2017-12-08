using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Collections;
using System.Globalization;
using KeyMapper.Classes;
using KeyMapper.Controls;
using KeyMapper.Providers;
using Microsoft.Win32;
using System.Drawing.Imaging;
using KeyMapper.Classes.Interop;

namespace KeyMapper.Forms
{
    public partial class KeyboardForm : KMBaseForm
    {
        float _buttonScale;
        float _keySize;
        int _paddingWidth;
        bool _hasNumberPad;
        bool _keysOnly;
        KeySniffer _sniffer;
        bool _cancelSlideshow;

        // Because we are intercepting a keypress before it is processed, can't ask
        // what state the keyboard is in using Form.IsKeySet or WIN32API funcs like
        // GetKeyState. So, using fields for the state of each key.

        bool _isCapsLockOn;
        bool _isNumLockOn;
        bool _isScrollLockOn;

        // Different arrangement of ALT and CTRL keys.
        bool _isMacKeyboard;

        Size _lastSize;

        int[] _rowTerminators;

        private readonly ToolTip FormToolTip;

        private readonly IOperatingSystemCapability operatingSystemCapability = new OperatingSystemCapabilityProvider();

        public KeyboardForm()
        {
            FormToolTip = new ToolTip();

            InitializeComponent();
            FormsManager.RegisterMainForm(this);

           if (DpiInfo.Dpi < 96)
            {
                menu.Height = (int) (menu.Height*(96F/DpiInfo.Dpi));
                    // Menu will always show even is fonts are set to less than 100%
            }
            else if (DpiInfo.Dpi > 96)
            {
                menu.Height = (int) (menu.Height*(DpiInfo.Dpi/96F));
            }

            LoadUserSettings();

            ResizeToAspect();

            if (DpiInfo.Dpi != 96)
            {
                PositionKeyboardCombo();
            }

            // This needs to be done after location and size of this form are fully determined.
            FormsManager.OpenChildForms();

            _lastSize = Size;

            GetKeyboardData();

            // Create event handlers 
            this.ResizeEnd += this.KeyboardFormResizeEnd;
            this.KeyboardListCombo.SelectedIndexChanged += KeyboardListSelectedIndexChanged;

            MappingsManager.MappingsChanged += OnMappingsChanged;
            UserColourSettingManager.ColoursChanged += OnColoursChanged;

            // Sniff for Caps/Num/Scroll lock keys being pressed while app doesn't have focus
            _sniffer = new KeySniffer();
            _sniffer.KeyPressed += ReceiveKeyPress;
            _sniffer.ActivateHook();

            this.Redraw();
        }


        private void PositionKeyboardCombo()
        {
            // Combo needs to go in the middle of the status bar..
            KeyboardListCombo.Top = (StatusBar.Top + ((StatusBar.Height - KeyboardListCombo.Height) / 2));

        }
        private void LoadUserSettings()
        {
            Properties.Settings userSettings = new Properties.Settings();

            // As user.config is writeable (if you can find it!)
            // don't want to trust the settings.

            bool firstrun = true;
            Point savedPosition = Point.Empty;
            int savedWidth = 0;
            MappingFilter oldFilter = MappingFilter.All;

            firstrun = (userSettings.UserHasSavedSettings == false);
            savedPosition = userSettings.KeyboardFormLocation;
            savedWidth = userSettings.KeyboardFormWidth;

            _hasNumberPad = userSettings.KeyboardFormHasNumberPad;
            _isMacKeyboard = userSettings.KeyboardFormHasMacKeyboard;
            oldFilter = (MappingFilter)userSettings.LastMappingsFilter;

            //if (firstrun == false)
            //{
            //    // AppController.SwitchKeyboardLayout((KeyboardLayoutType)userSettings.KeyboardLayout);
            //}

            if (firstrun || savedPosition.IsEmpty || savedPosition.X == -32000)
                FormsManager.PositionMainForm();
            else
                this.Location = savedPosition;

            if (firstrun || savedWidth < this.MinimumSize.Width)
            {
                FormsManager.SizeMainForm();
            }
            else
            {
                this.Width = savedWidth;
            }


            // If there are boot mappings and no user mappings and the last view mode was boot, then
            // start in boot mode - as long as user has the rights to change them (or is running Vista)

            if (oldFilter == MappingFilter.Boot
                && MappingsManager.GetMappingCount(MappingFilter.Boot) > 0
                && MappingsManager.GetMappingCount(MappingFilter.User) == 0
                && (AppController.UserCanWriteBootMappings || operatingSystemCapability.ImplementsUAC))
            {
                MappingsManager.SetFilter(MappingFilter.Boot);
            }
        }

        void CalculateDimensions()
        {
            float keywidth;

            if (_keysOnly)
            {
                keywidth = 15.7F;
            }
            else
            {
                // These two numbers correspond more-or less to the number of keys wide 
                // a keyboard is, with or without number pad, plus a bit extra for the padding between keys.
                keywidth = _hasNumberPad ? 23.75F : 20F;
            }

            // Calculate total width and key size
            const int buttonwidth = 128; // Starting width

            _buttonScale = ((float)this.ClientSize.Width / (int)(buttonwidth * keywidth)); // How much buttons have to be scaled to fit
            _keySize = (buttonwidth * _buttonScale); // Actual size of buttons
            _paddingWidth = (int)(_keySize / 16); // Gap between rows and columns.
        }

        void Redraw()
        {
            // Can be errors during slideshow, so if we get one then cancel it.
            try
            {
                NativeMethods.LockWindowUpdate(this.Handle);
            }
            catch (ObjectDisposedException)
            {
                _cancelSlideshow = true;
                return;
            }

            FormToolTip.RemoveAll();
            FormToolTip.SetToolTip(KeyboardListCombo, "Change the displayed keyboard");

            // Need to make sure these dispose as they have bitmap resources, so not using Controls.Clear()
            // as it didn't release them properly..
            for (int i = this.KeyboardPanel.Controls.Count - 1; i >= 0; i--)
            {
                this.KeyboardPanel.Controls[i].Dispose();
            }

            // Start in the top left corner..
            int left = _paddingWidth;
            int top = _paddingWidth + this.menu.Height;

            int numpadleft = 0;
            int navleft = 0;

            float mainkeywidth = (14.5F * (_keySize + _paddingWidth)) + (_paddingWidth * 2);
            float navwidth = ((_keySize + _paddingWidth) * 3);

            // Work out how far back the number pad extends 
            if (_hasNumberPad)
            {
                numpadleft = (int)Math.Round(this.ClientSize.Width - (((_keySize + _paddingWidth) * 4.2)), 0);
                // Nav controls are three wide and they have to fit midway in the
                // gap between the end of the main body and the numberpad.
                navleft = (int)Math.Round((mainkeywidth + ((numpadleft - mainkeywidth - navwidth) / 2)), 0);
            }
            else
            {
                // Work out how far back the navkeys extend instead:
                navleft = (int)Math.Round(this.ClientSize.Width - (((_keySize + _paddingWidth) * 3.2)), 0);
            }

            KeyboardLayoutType desiredlayout = AppController.KeyboardLayout;

            PhysicalKeyboardLayout kl = PhysicalKeyboardLayout.GetPhysicalLayout(desiredlayout, _isMacKeyboard);

            _rowTerminators = PhysicalKeyboardLayout.GetRowTerminators(desiredlayout);

            if (_keysOnly == false)
            {
                // Function keys.
                DrawRow(kl.FunctionKeys, left, top);

                // Utility Keys
                if (_hasNumberPad == false || desiredlayout == KeyboardLayoutType.US)
                {
                    DrawRow(kl.UtilityKeys, navleft, top);
                }
                else
                {
                    // Shunt keys along one key-width (plus padding) for UK layout so they right-justify
                    DrawRow(kl.UtilityKeys, numpadleft + (int)Math.Round(_keySize, 0) + _paddingWidth, top);
                }

                // To get a spacer row between the F keys: add double padding
                top += (int)Math.Round(_keySize + (_paddingWidth * 2), 0);

            }

            // TypewriterKeys keys
            DrawRow(kl.TypewriterKeys, left, top);

            if (_keysOnly == false)
            {
                // Navigation - Insert, Home, End etc
                DrawRow(kl.NavigationKeys, navleft, top);

                // Number pad
                if (_hasNumberPad)
                {
                    DrawRow(kl.NumberPadKeys, numpadleft, top);
                }

                // Skip down and back for arrow keys
                top += (int)Math.Round((_keySize + _paddingWidth) * 3, 0);
                DrawRow(kl.ArrowKeys, navleft, top);

            }

            // Need to establish what the keys really are.. should know but it doesn't hurt to check,
            // especially as it went out of kilter at least once in development. 
            _isCapsLockOn = Form.IsKeyLocked(Keys.CapsLock); //  KeyboardHelper.IsKeySet(KeyboardHelper.ToggleKey.CapsLock);
            _isNumLockOn = Form.IsKeyLocked(Keys.NumLock); // .IsKKeyboardHelper.IsKeySet(KeyboardHelper.ToggleKey.NumLock);
            _isScrollLockOn = Form.IsKeyLocked(Keys.Scroll); //  KeyboardHelper.IsKeySet(KeyboardHelper.ToggleKey.ScrollLock);

            SetStatusLabelsText();
            SetMenuButtonStates();

            NativeMethods.LockWindowUpdate(IntPtr.Zero);

        }

        void DrawRow(IEnumerable<KeyboardRow> Rows, int leftstart, int top)
        {
            int left = leftstart;

            // Need to set the exact width of the row.
            int width = (int)(14.7F * (_keySize + _paddingWidth));
            if (width % 2 != 0)
                width += 1;

            foreach (KeyboardRow row in Rows)
            {
                foreach (KeyboardLayoutElement key in row.Keys)
                {
                    if (key == null)
                    {
                        // Spacer - eg to centre the up arrow key 
                        left += (int)Math.Round((decimal)_keySize, 0) + _paddingWidth;
                    }
                    else
                    {
                        // The last key in the row must be stretched appropriately.
                        // Which one's that, then?

                        int index = Array.IndexOf(_rowTerminators, KeyHasher.GetHashFromKeyData(key.Scancode, key.Extended));

                        if (index < 0)
                        {
                            DrawKey(key.Scancode, key.Extended, ref left, top, key.Button,
                                key.HorizontalStretch * _paddingWidth, key.VerticalStretch * _paddingWidth);
                        }
                        else
                        {

                            // Ok. How much space is left to fill?
                            // left is where we are to start drawing, width is the line width.
                            int keywidth = (int)((width - left));

                            // Calculate the horizontal stretch value based on the sizes:

                            int buttonwidth = 0;

                            switch (key.Button)
                            {
                                case BlankButton.Blank:
                                case BlankButton.TallBlank:
                                    buttonwidth = 128;
                                    break;
                                case BlankButton.MediumWideBlank:
                                    buttonwidth = 192;
                                    break;
                                case BlankButton.DoubleWideBlank:
                                    buttonwidth = 256;
                                    break;
                                case BlankButton.TripleWideBlank:
                                    buttonwidth = 384;
                                    break;
                                case BlankButton.QuadrupleWideBlank:
                                    buttonwidth = 512;
                                    break;
                                default:
                                    break;
                            }

                            int stretch = (int)(keywidth - (buttonwidth * _buttonScale));

                            DrawKey(key.Scancode, key.Extended, ref left, top, key.Button,
                               stretch, key.VerticalStretch * _paddingWidth);
                        }

                        left += key.RightPadding * _paddingWidth;
                    }

                }

                top += _paddingWidth + (int)Math.Round((decimal)_keySize, 0);
                left = leftstart;
            }

        }

        void DrawKey(int scancode, int extended, ref int left, int top, BlankButton button, int horizontalStretch, int verticalStretch)
        {

            KeyPictureBox box = new KeyPictureBox(scancode, extended, button, this._buttonScale, horizontalStretch, verticalStretch);

            box.Left = left;
            box.Top = top;

            this.KeyboardPanel.Controls.Add(box);

            // Set the event handler unless filter is boot mappings and user can't write to boot mappings and this isn't Vista
            if (((MappingsManager.Filter == MappingFilter.Boot
                && !AppController.UserCanWriteBootMappings
                && !operatingSystemCapability.ImplementsUAC)) == false)
            {
                box.DoubleClick += KeyDoubleClick;
            }

            string toolTipText = box.Map.MappingDescription;

            if (string.IsNullOrEmpty(toolTipText) == false)
                FormToolTip.SetToolTip(box, toolTipText);

            // left is a ref parameter.
            left += box.Image.Width + _paddingWidth; // Width varies eg for double-width blanks

        }

        void ResizeToAspect()
        {
            // Need to keep the aspect ratio in the shape of a keyboard. 

            if (WindowState == FormWindowState.Minimized)
                return;

            float factor;
            if (_keysOnly)
                factor = 34.5F;
            else
                factor = _hasNumberPad ? 43F : 36F;

            this.SetClientSizeCore(this.ClientSize.Width, StatusBar.Height + this.menu.Height +
                (int)(this.ClientSize.Width * (12F / (factor))));

            menu.Width = this.ClientSize.Width;

            KeyboardPanel.Height = this.ClientSize.Height - StatusBar.Height + this.menu.Height;
            KeyboardPanel.Width = this.ClientSize.Width;

            CalculateDimensions();

        }

        void SetMappingStatusLabelText()
        {

            int allmaps = MappingsManager.GetMappingCount(MappingFilter.All);
            int bootmaps = MappingsManager.GetMappingCount(MappingFilter.Boot);
            int usermaps = MappingsManager.GetMappingCount(MappingFilter.User);

            // TODO: Localizing issue. How to do plurals in other cultures???

            string mapstatustext;

            if (allmaps > 0)
            {
                string bootmaptext = string.Empty;
                if (bootmaps != 0)
                {
                    bootmaptext =
                        operatingSystemCapability.SupportsUserMappings
                        ? string.Format("{0} boot mapping{1}", bootmaps, (bootmaps != 1 ? "s" : ""))
                        : string.Format("{0} mapping{1}", bootmaps, (bootmaps != 1 ? "s" : ""));
                }

                if (usermaps != 0)
                {
                    string usermaptext = usermaps.ToString(CultureInfo.InvariantCulture.NumberFormat) + " user mapping" +
                                         (usermaps != 1 ? "s" : "");

                    mapstatustext =
                        bootmaptext +
                        (string.IsNullOrEmpty(bootmaptext) ? "" : ", ") +
                        usermaptext;
                }
                else
                {
                    mapstatustext = bootmaptext;
                }
            }
            else
            {
                // Need to have *something* on the status bar otherwise it doesn't work
                // properly in W2K (it doesn't show on startup)
                mapstatustext = "No mappings";
            }

            StatusLabelMappings.Text = mapstatustext;
        }

        void SetReadonlyStatusLabelText()
        {
            if (MappingsManager.IsRestartRequired())
            {
                StatusLabelRestartLogoff.Text = "Restart to complete the mappings";
                StatusLabelRestartLogoff.Visible = true;
            }
            else if (MappingsManager.IsLogOnRequired())
            {
                StatusLabelRestartLogoff.Text = "Log on again to complete the mappings";
                StatusLabelRestartLogoff.Visible = true;
            }
            else
            {
                StatusLabelRestartLogoff.Text = string.Empty;
                StatusLabelRestartLogoff.Visible = false;
            }

            StatusLabelReadOnly.Visible = (AppController.UserCannotWriteMappings && !operatingSystemCapability.ImplementsUAC);
        }

        void SetFilterStatusLabelText()
        {
            if (operatingSystemCapability.SupportsUserMappings == false)
            {
                StatusLabelMappingDisplayType.Visible = false;
            }
            else
            {
                switch (MappingsManager.Filter)
                {
                    case MappingFilter.All:
                        StatusLabelMappingDisplayType.Visible = false;
                        break;

                    case MappingFilter.Boot:
                        StatusLabelMappingDisplayType.Text =
                            (AppController.UserCanWriteBootMappings || operatingSystemCapability.ImplementsUAC ? "Editing" : "Showing") + " Boot Mappings";
                        StatusLabelMappingDisplayType.Visible = true;
                        break;

                    case MappingFilter.User:
                        StatusLabelMappingDisplayType.Text = "Editing User Mappings";
                        StatusLabelMappingDisplayType.Visible = true;
                        break;

                    default:
                        break;
                }
            }

        }

        void SetStatusLabelsText()
        {
            SetMappingStatusLabelText();
            SetReadonlyStatusLabelText();
            SetFilterStatusLabelText();
        }

        void SaveUserSettings()
        {
            Properties.Settings userSettings = new Properties.Settings
                {
                    KeyboardFormLocation = this.Location,
                    KeyboardFormWidth = this.Width,
                    KeyboardFormHasNumberPad = this._hasNumberPad,
                    KeyboardFormHasMacKeyboard = this._isMacKeyboard,
                    ColourMapFormOpen = FormsManager.IsColourMapFormOpen(),
                    MappingListFormOpen = FormsManager.IsMappingListFormOpen(),
                    LastMappingsFilter = (int)MappingsManager.Filter,
                    UserHasSavedSettings = true
                };

            // userSettings.KeyboardLayout = (int)AppController.KeyboardLayout;
            userSettings.Save();
        }

	    private void GetKeyboardData()
        {

            // InstalledKeyboards contains the list of keyboard names and locales
            // Combo just gets the names at that's what combos like.

            ArrayList keyboardComboData;
            ToolStripItem[] keyboardMenuData;

            ArrayList tempArr = new ArrayList(KeyboardHelper.InstalledKeyboards.Keys);
            tempArr.Sort();
            keyboardComboData = new ArrayList(KeyboardHelper.InstalledKeyboards.Count + 1);
            // Add the current keyboard and a separator:
            keyboardComboData.Add(new ComboItemSeparator.SeparatorItem(KeyboardHelper.GetKeyboardName()));
            keyboardComboData.AddRange(tempArr);

            KeyboardListCombo.DataSource = keyboardComboData;

            keyboardMenuData = new ToolStripMenuItem[KeyboardHelper.InstalledKeyboards.Count];

            int count = 0;
            foreach (string name in tempArr)
            {
                keyboardMenuData[count] = new ToolStripMenuItem(name, null, selectLayoutToolStripItemClick);
                count++;
            }

            selectLayoutToolStripMenuItem.DropDownItems.AddRange(keyboardMenuData);
        }


        void ToggleNumberpad()
        {
            _hasNumberPad = !_hasNumberPad;
            ResizeToAspect();
            Redraw();
        }

        void ChangeKeyboard(string name)
        {
            ChangeKeyboard(name, false);
        }


        void ChangeKeyboard(string name, bool calledFromCombo)
        {

            if (KeyboardHelper.InstalledKeyboards.Contains(name))
            {
                AppController.SetLocale(KeyboardHelper.InstalledKeyboards[name].ToString());
                if (calledFromCombo == false)
                {
                    KeyboardListCombo.SelectedIndexChanged -= KeyboardListSelectedIndexChanged;
                    KeyboardListCombo.SelectedItem = name;
                    KeyboardListCombo.SelectedIndexChanged += KeyboardListSelectedIndexChanged;
                }
                this.Redraw();
            }
        }

        void SimulateToggleKeyKeypress(KeyboardHelper.ToggleKey key)
        {
            _sniffer.DeactivateHook();
            KeyboardHelper.PressKey(key);

            switch (key)
            {
                case KeyboardHelper.ToggleKey.NumLock:
                    _isNumLockOn = !_isNumLockOn;
                    break;
                case KeyboardHelper.ToggleKey.CapsLock:
                    _isCapsLockOn = !_isCapsLockOn;
                    break;
                case KeyboardHelper.ToggleKey.ScrollLock:
                    _isScrollLockOn = !_isScrollLockOn;
                    break;
                default:
                    break;
            }

            _sniffer.ActivateHook();
            SetToggleMenuButtonStates();
        }

        void ChangeKeyOrientation()
        {
            switch (AppController.KeyboardLayout)
            {
                case KeyboardLayoutType.US:
                    AppController.SwitchKeyboardLayout(KeyboardLayoutType.European);
                    Redraw();
                    break;
                case KeyboardLayoutType.European:
                    AppController.SwitchKeyboardLayout(KeyboardLayoutType.US);
                    Redraw();
                    break;
                default:
                    AppController.SwitchKeyboardLayout(KeyboardLayoutType.US);
                    Redraw();
                    break;
            }
            // In order to remember the choice..
            AppController.AddCustomLayout();
        }

	    // Called by FormsManager when a form closes.
        public void RegenerateMenuExternal()
        {
            SetWindowMenuButtonStates();
        }

        void SetToggleMenuButtonStates()
        {
            // Toggle Keys
            capsLockToolStripMenuItem.Checked = _isCapsLockOn;
            numLockToolStripMenuItem.Checked = _isNumLockOn;
            scrollLockToolStripMenuItem.Checked = _isScrollLockOn;
            setCurrentToggleKeysAtBootToolStripMenuItem.Enabled = AppController.UserCanWriteBootMappings || operatingSystemCapability.ImplementsUAC;
        }

        void SetMappingsMenuButtonStates()
        {
            // Mappings - view all, user, boot.
            if (operatingSystemCapability.SupportsUserMappings)
            {
                switch (MappingsManager.Filter)
                {
                    case MappingFilter.All:
                        clearAllToolStripMenuItem.Text = "C&lear All Mappings";
                        break;

                    case MappingFilter.Boot:
                        clearAllToolStripMenuItem.Text = "C&lear All Boot Mappings";
                        break;

                    case MappingFilter.User:
                        clearAllToolStripMenuItem.Text = "C&lear All User Mappings";
                        break;
                }
            }
            else
            {
                clearAllToolStripMenuItem.Text = "C&lear All Mappings";
            }
            // Disable "Clear Mappings" and "Revert To Saved Mappings" if user can't write mappings at all
            // and the latter if there haven't been any changes

            clearAllToolStripMenuItem.Enabled = !AppController.UserCannotWriteMappings;

            revertToSavedToolStripMenuItem.Enabled = (
                AppController.UserCannotWriteMappings == false &&
                (MappingsManager.IsRestartRequired() || MappingsManager.IsLogOnRequired()));

            onlyShowBootMappingsToolStripMenuItem.Text = "Boot Mappings" +
              (AppController.UserCanWriteBootMappings || operatingSystemCapability.ImplementsUAC ? string.Empty : " (Read Only)");

            // Mappings - check current view
            showAllMappingsToolStripMenuItem.Checked = (MappingsManager.Filter == MappingFilter.All);
            onlyShowBootMappingsToolStripMenuItem.Checked = (MappingsManager.Filter == MappingFilter.Boot);
            onlyShowUserMappingsToolStripMenuItem.Checked = (MappingsManager.Filter == MappingFilter.User);

            // Whether to allow the option of viewing user mappings (ie not on W2K) 

            chooseMappingsToolStripMenuItem.Visible = (operatingSystemCapability.SupportsUserMappings);
         
            selectFromCaptureToolStripMenuItem.Enabled = !AppController.UserCannotWriteMappings;
        }

        void SetWindowMenuButtonStates()
        {
            colourMapFormToolStripMenuItem.Checked = FormsManager.IsColourMapFormOpen();
            mappingListFormToolStripMenuItem.Checked = FormsManager.IsMappingListFormOpen();

        }

        void SetEditMenuButtonStates()
        {
            undoToolStripMenuItem.Enabled = (MappingsManager.UndoStackCount > 0);
            redoToolStripMenuItem.Enabled = (MappingsManager.RedoStackCount > 0);
        }

        void SetKeyboardLayoutMenuButtonStates()
        {
            toggleNumberPadToolStripMenuItem.Enabled = !_keysOnly;
            showMainKeysOnlyToolStripMenuItem.Checked = _keysOnly;

            toggleNumberPadToolStripMenuItem.Checked = _hasNumberPad;
            useMacKeyboardToolStripMenuItem.Checked = _isMacKeyboard;
        }

        void SetMenuButtonStates()
        {
            SetToggleMenuButtonStates();
            SetMappingsMenuButtonStates();
            SetWindowMenuButtonStates();
            SetEditMenuButtonStates();
            SetKeyboardLayoutMenuButtonStates();
            // SetAdvancedMenuButtonStates();
        }


        //private void SetAdvancedMenuButtonStates()
        //{
        //}

	    private void KeyboardListSelectedIndexChanged(object sender, EventArgs e)
        {
            this.ChangeKeyboard(KeyboardListCombo.Text, true);
        }

        private void KeyDoubleClick(object sender, EventArgs e)
        {
            KeyPictureBox box = sender as KeyPictureBox;
            if (box == null)
                return;
            FormsManager.ShowEditMappingForm(box.Map, false);
        }

        private void KeyboardFormClosed(object sender, FormClosedEventArgs e)
        {
            if (_sniffer != null)
            {
                _sniffer.KeyPressed -= ReceiveKeyPress;
                _sniffer.DeactivateHook();
                _sniffer = null;
            }
            KeyboardHelper.UnloadLayout();
        }

        private void KeyboardFormClosing(object sender, FormClosingEventArgs e)
        {
            // Save settings before we close, so references to subforms are still live.
            SaveUserSettings();
        }

        void KeyboardFormKeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Escape)
                _cancelSlideshow = true;

        }

        private void OnMappingsChanged(object sender, EventArgs e)
        {
            this.Redraw();
        }

        void OnColoursChanged(object sender, EventArgs e)
        {
            this.Redraw();
        }


        private void KeyboardFormResizeEnd(object sender, EventArgs e)
        {
            if (Size != _lastSize) // Not just a move (which fires this too)
            {
                _lastSize = Size;
                ResizeToAspect();
                Redraw();
            }
        }

        private void ReceiveKeyPress(object sender, KeyMapperKeyPressedEventArgs e)
        {
            if (e != null)
            {
                KBHookStruct key = e.Key;

                // Because we are intercepting a keypress before it is processed, can't ask
                // what state the keyboard is in using Form.IsKeySet or WIN32API funcs like
                // GetKeyState. So, using fields for the state of each key.

                // (In fact, the only thing this achieves is live updating of the Toggle Lock Menu if user presses
                // a lock key while menu is open. It's a small thing, but would be a shame to lose it)

                switch (key.VirtualKeyCode)
                {
                    case (int)KeyboardHelper.ToggleKey.CapsLock:
                        _isCapsLockOn = !_isCapsLockOn;
                        SetToggleMenuButtonStates();
                        break;
                    case (int)KeyboardHelper.ToggleKey.NumLock:
                        _isNumLockOn = !_isNumLockOn;
                        SetToggleMenuButtonStates();
                        break;
                    case (int)KeyboardHelper.ToggleKey.ScrollLock:
                        _isScrollLockOn = !_isScrollLockOn;
                        if (_isScrollLockOn != Form.IsKeyLocked(Keys.Scroll))
                            SetToggleMenuButtonStates();
                        break;
                }

            }

        }

        //void KeyPictureBoxMouseDown(object sender, MouseEventArgs e)
        //{
        //    if (e.Button == MouseButtons.Right)
        //    {
        //        _contextBox = (sender as KeyPictureBox);
        //        if (_contextBox != null)
        //        {
        //            PopulateContextMenu();
        //        }
        //    }
        //}

	    private void exitMenuItemClick(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void undoMenuItemClick(object sender, EventArgs e)
        {
            MappingsManager.UndoMappingChange();
        }

        private void redoMenuItemClick(object sender, EventArgs e)
        {
            MappingsManager.RedoMappingChange();
        }

        private void changeOrientationMenuItemClick(object sender, EventArgs e)
        {
            ChangeKeyOrientation();
        }

        private void capsLockMenuItemClick(object sender, EventArgs e)
        {
            SimulateToggleKeyKeypress(KeyboardHelper.ToggleKey.CapsLock);
        }

        private void toggleNumberPadMenuItemClick(object sender, EventArgs e)
        {
            ToggleNumberpad();
        }

        private void numLockMenuItemClick(object sender, EventArgs e)
        {
            SimulateToggleKeyKeypress(KeyboardHelper.ToggleKey.NumLock);
        }

        private void scrollLockMenuItemClick(object sender, EventArgs e)
        {
            SimulateToggleKeyKeypress(KeyboardHelper.ToggleKey.ScrollLock);
        }

        private void selectLayoutToolStripItemClick(object sender, EventArgs e)
        {
            this.ChangeKeyboard(sender.ToString());
        }

        private void selectFromCaptureMenuItemClick(object sender, EventArgs e)
        {
            FormsManager.ShowEditMappingForm(new KeyMapping(), true);
        }

        private void revertToSavedMenuItemClick(object sender, EventArgs e)
        {
            MappingsManager.RevertToStartupMappings();
            OnMappingsChanged(null, null);
        }

        private void clearAllMenuItemClick(object sender, EventArgs e)
        {
            MappingsManager.ClearMappings();
            OnMappingsChanged(null, null);
        }

        private void showMainKeysOnlyMenuItemClick(object sender, EventArgs e)
        {
            _keysOnly = !_keysOnly;
            ResizeToAspect();
            Redraw();
        }

        private void onlyShowBootMappingsMenuItemClick(object sender, EventArgs e)
        {
            MappingsManager.SetFilter(MappingFilter.Boot);
            Redraw();
        }

        private void onlyShowUserMappingsMenuItemClick(object sender, EventArgs e)
        {
            MappingsManager.SetFilter(MappingFilter.User);
            Redraw();
        }

        private void showAllMappingsMenuItemClick(object sender, EventArgs e)
        {
            MappingsManager.SetFilter(MappingFilter.All);
            Redraw();
        }

        private void viewListMenuItemClick(object sender, EventArgs e)
        {
            FormsManager.ToggleMappingListForm();
            SetWindowMenuButtonStates();
        }

        private void showColourMapFormMenuItemClick(object sender, EventArgs e)
        {
            FormsManager.ToggleColourMapForm();
            SetWindowMenuButtonStates();
        }


        private void useMacKeyboardMenuItemClick(object sender, EventArgs e)
        {
            _isMacKeyboard = !_isMacKeyboard;
            Redraw();
        }

        private void revertToDefaultKeyboardLayoutMenuItemClick(object sender, EventArgs e)
        {
            // Revert to default keyboard layout 
            this.ChangeKeyboard(KeyboardHelper.GetKeyboardName());
            this.Redraw();
        }

        private void exportAsRegistryFileMenuItemClick(object sender, EventArgs e)
        {
            MappingsManager.ExportMappingsAsRegistryFile(MappingFilter.All, false);
        }

        private void selectFromListsMenuItemClick(object sender, EventArgs e)
        {
            FormsManager.ShowEditMappingForm(new KeyMapping(), false);
        }

        private void arrangeWindowsMenuItemClick(object sender, EventArgs e)
        {
            FormsManager.ArrangeAllOpenForms();
            // Also, delete the saved position for the Add/Edit mapping form so it's in it's default location
            // next time it's shown.
            Properties.Settings userSettings = new KeyMapper.Properties.Settings();
            userSettings.EditMappingFormLocation = Point.Empty;
            userSettings.Save();
        }

        private void showHelpMenuItemClick(object sender, EventArgs e)
        {
            FormsManager.ShowHelpForm();
        }

        private void aboutKeyMapperMenuItemClick(object sender, EventArgs e)
        {
            FormsManager.ShowAboutForm();
        }

        private void putKeyboardListOnClipboardToolStripMenuItemClick(object sender, EventArgs e)
        {
            KeyboardHelper.ShowKeyboardList();
        }

        private void viewLogFileToolStripMenuItemClick(object sender, EventArgs e)
        {
            LogProvider.ViewLogFile();
        }

        private void setCurrentToggleKeysAtBootToolStripMenuItemClick(object sender, EventArgs e)
        {
            // The HKEY_CURRENT_USER version of this setting is set at logoff.
            // This sets the value which applies before any user has logged on.

            string value =
                    ((_isCapsLockOn ? 1 : 0) + (_isNumLockOn ? 2 : 0) + (_isScrollLockOn ? 4 : 0)).ToString(CultureInfo.InvariantCulture);

            if (AppController.UserCanWriteBootMappings == false)
            {
                if (AppController.ConfirmWriteToProtectedSectionOfRegistryOnVistaOrLater("the default toggle keys") == false)
                    return;

                AppController.WriteRegistryEntryVista(
                    RegistryHive.Users, @".DEFAULT\Control Panel\Keyboard", "InitialKeyboardIndicators", value);
            }
            else
            {
                try
                {
                    RegistryKey regkey = Registry.Users.OpenSubKey(@".DEFAULT\Control Panel\Keyboard", true);
                    if (regkey != null)
                        regkey.SetValue("InitialKeyboardIndicators", value);

                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error trying to set default toggle keys: {0}", ex.ToString());
                }
            }

        }

        private void printScreenToFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveKeyboardImageAsFile(false);
        }

        private void SaveKeyboardImageAsFile(bool autoSave)
        {
            using (Bitmap bmp = new Bitmap(this.Width, this.Height))
            {
                this.DrawToBitmap(bmp, new Rectangle(Point.Empty, this.Size));

                Size actualSize = new Size(this.ClientSize.Width, this.ClientSize.Height - this.menu.Height - this.StatusBar.Height);

                using (Bitmap bmp2 = new Bitmap(actualSize.Width, actualSize.Height))
                {
                    Point p = this.PointToScreen(Point.Empty);

                    int x = p.X - this.Left;
                    int y = p.Y - this.Top + this.menu.Height;

                    using (Graphics g = Graphics.FromImage(bmp2))
                    {
                        g.DrawImage(bmp, 0, 0, new Rectangle(x, y, actualSize.Width, actualSize.Height), GraphicsUnit.Pixel);
                    }

                    string filename = this.KeyboardListCombo.Text;

                    if (autoSave)
                    {
                        // bmp2.Save(@"somefolder" + filename + ".png", ImageFormat.Png) ;
                    }
                    else
                    {
                        SaveFileDialog fd = new SaveFileDialog
                            {
                                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                                OverwritePrompt = false,
                                AutoUpgradeEnabled = true,
                                FileName = filename + " keyboard layout",
                                Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg,*.jpeg)|*.jpg;*.jpeg|Bitmap (*.bmp)|*.bmp"
                            };

                        if (fd.ShowDialog() == DialogResult.OK)
                        {
                            if (fd.FileName.EndsWith("jpg", StringComparison.OrdinalIgnoreCase) ||
                                fd.FileName.EndsWith("jpeg", StringComparison.OrdinalIgnoreCase))
                            {
                                bmp2.Save(fd.FileName, ImageFormat.Jpeg);
                            }
                            else if (fd.FileName.EndsWith("bmp", StringComparison.OrdinalIgnoreCase))
                            {
                                bmp2.Save(fd.FileName, ImageFormat.Bmp);
                            }
                            else if (fd.FileName.EndsWith("png", StringComparison.OrdinalIgnoreCase))
                            {
                                bmp2.Save(fd.FileName, ImageFormat.Png);
                            }
                        }
                    }
                }
            }
        }
        
        private void clearLogFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            LogProvider.ClearLogFile();
        }

	    private void forceUserMappingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MappingsManager.SaveUserMappingsToKeyMapperKey(true);
        }

        private void keyboardSlideshowToolStripMenuItemClick(object sender, EventArgs e)
        {
            string caption = this.Text;
            int currentKeyboard = KeyboardListCombo.SelectedIndex;
            this.Text += " (press Escape to stop slideshow)";
            _cancelSlideshow = false;
            this.KeyPress += KeyboardFormKeyPress;
            for (int i = 0; i < KeyboardListCombo.Items.Count; i++)
            {
                if (_cancelSlideshow)
                {
                    KeyboardListCombo.SelectedIndex = currentKeyboard;
                    Application.DoEvents();
                    break;
                }

                KeyboardListCombo.SelectedIndex = i;
                Application.DoEvents();
                System.Threading.Thread.Sleep(250);
            }
            this.KeyPress -= KeyboardFormKeyPress;
            this.Text = caption;

        }

        private void resetUserSetingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Properties.Settings.Default.Reset();
        }

    }

}

