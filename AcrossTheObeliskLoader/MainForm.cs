﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace AcrossTheObeliskLoader
{
    public partial class MainForm : Form
    {
        private string BasePath;
        private string[] PlayerFolders;
        private string PlayerPath;

        private static MainForm _instance;

        #region Hook
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private static readonly LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
            IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                if (vkCode != 0)
                {
                    _instance.CheckHotkey((Keys)vkCode);
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }
        #endregion

        public MainForm()
        {
            _instance = this;   // for static function call

            InitializeComponent();

            InitializeDropdown();

            _hookID = SetHook(_proc);

            label3.Parent = picLogo;

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            ShowError(((Exception)e.ExceptionObject).Message);
            Console.WriteLine(e.ExceptionObject.ToString());
        }

        private void InitializeDropdown()
        {
            SetKeyDropdown(cboBackupKey, "BackupHotkey");
            SetKeyDropdown(cboLoadKey, "LoadHotkey");
        }

        private void SetKeyDropdown(ComboBox comboBox, string settingKey)
        {
            var keys = Enum.GetValues(typeof(Keys)).Cast<Keys>()
                .Where(x => x == Keys.None || (x >= Keys.F1 && x <= Keys.F12))
                .ToList();

            comboBox.DataSource = keys;
            comboBox.SelectedIndex = 0;

            var key = (Keys)Properties.Settings.Default[settingKey];
            comboBox.SelectedIndex = keys.IndexOf(key);

            comboBox.SelectedIndexChanged += (_sender, _e) =>
            {
                Properties.Settings.Default[settingKey] = (int)((ComboBox)_sender).SelectedItem;
                Properties.Settings.Default.Save();
            };
        }

        private void CheckHotkey(Keys key)
        {
            if (key == (Keys)cboBackupKey.SelectedItem)
            {
                btnBackup.PerformClick();
            }
            else if (key == (Keys)cboLoadKey.SelectedItem)
            {
                btnLoad.PerformClick();
            }
        }

        private void btnBackup_Click(object sender, EventArgs e)
        {
            var saveFilePrefix = cboSaveType.SelectedItem as string;
            if (string.IsNullOrEmpty(saveFilePrefix)) { return; }

            var saveFilePath = Path.Combine(PlayerPath, saveFilePrefix + ".ato");
            if (!File.Exists(saveFilePath))
            {
                ShowError("Can't find save file.");
                LoadSaveFilesByType();
                return;
            }

            var date = File.GetLastWriteTime(saveFilePath);
            var destFilePath = Path.Combine(PlayerPath, $"{saveFilePrefix}_{date:yyyyMMdd_HHmmss}.ato");

            // if file exist, don't copy again
            if (File.Exists(destFilePath)) { return; }
            File.Copy(saveFilePath, destFilePath);

            ShowInfo("Save backup.");
            System.Media.SystemSounds.Beep.Play();
            LoadSaveFilesByType();
        }

        private void ShowInfo(string message)
        {
            lblMessage.ForeColor = SystemColors.ControlText;
            lblMessage.Text = message;
        }

        private void ShowError(string message)
        {
            lblMessage.ForeColor = Color.Red;
            lblMessage.Text = message;
        }

        private void btnLoad_Click(object sender, EventArgs e)
        {
            var saveFilePrefix = cboSaveType.SelectedItem as string;
            if (string.IsNullOrEmpty(saveFilePrefix)) { return; }

            var saveFiles = Directory.GetFiles(PlayerPath, saveFilePrefix + "_*");
            saveFiles = saveFiles.Where(x => x.EndsWith(".ato")).ToArray();
            if (saveFiles.Length == 0) {
                ShowInfo("There are no save here.");
                return;
            }

            // Get last save
            var lastSave = saveFiles.OrderByDescending(x => x).First();
            var originSave = Path.Combine(PlayerPath, saveFilePrefix + ".ato");
            if (File.Exists(originSave))
            {
                File.Delete(originSave);
            }
            File.Copy(lastSave, originSave);

            ShowInfo("Save loaded.");
            System.Media.SystemSounds.Asterisk.Play();
            LoadSaveFilesByType();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            var localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData).Replace("Roaming", "LocalLow");
            var basePath = Path.Combine(localAppDataPath, "Dreamsite Games", "AcrossTheObelisk");

            if (!Directory.Exists(basePath))
            {
                ShowError($"Can't find {basePath}");
                Close();
                return;
            }
            BasePath = basePath;

            LoadPlayerFolders();
            AutoSelectLastFolder();

            // Set default value
            if (cboPlayer.SelectedIndex < 0 && cboPlayer.Items.Count >= 1)
            {
                cboPlayer.SelectedIndex = 0;
                cboPlayer_SelectedIndexChanged(cboPlayer, null);
            }
            LoadSaveTypes();
            SetDefaultSaveType();
        }

        private void SetDefaultSaveType()
        {
            if (cboSaveType.SelectedIndex < 0 && cboSaveType.Items.Count >= 1)
            {
                cboSaveType.SelectedIndex = 0;
                cboSaveTypes_SelectedIndexChanged(cboSaveType, null);
            }
        }

        private void AutoSelectLastFolder()
        {
            var lastFolder = Properties.Settings.Default["LastSelectFolder"] as string;
            if (!string.IsNullOrEmpty(lastFolder) && PlayerFolders.Contains(lastFolder))
            {
                cboPlayer.SelectedItem = lastFolder;
                return;
            }

            Properties.Settings.Default["LastSelectFolder"] = null;
            Properties.Settings.Default.Save();
        }

        private void btnOpenFolder_Click(object sender, EventArgs e)
        {
            Process.Start(PlayerPath);
        }

        private void btnDelete_Click(object sender, EventArgs e)
        {
            var saveFilePrefix = cboSaveType.SelectedItem as string;
            if (string.IsNullOrEmpty(saveFilePrefix)) { return; }

            var saveFiles = Directory.GetFiles(PlayerPath, saveFilePrefix + "_*.ato");

            if (saveFiles.Length <= 1)
            {
                ShowInfo("No other save exists.");
                LoadSaveFilesByType();
                return;
            }

            var deleteFiles = saveFiles.OrderByDescending(x => x).Skip(1);
            foreach (var file in deleteFiles)
            {
                File.Delete(file);
            }

            ShowInfo($"Delete {deleteFiles.Count()} file(s) successfully.");
            LoadSaveFilesByType();
        }

        private void cboPlayer_SelectedIndexChanged(object sender, EventArgs e)
        {
            var folderName = cboPlayer.SelectedItem.ToString();
            var basePath = Path.Combine(BasePath, folderName);

            if (Directory.Exists(basePath))
            {
                PlayerPath = basePath;
                LoadSaveTypes();

                Properties.Settings.Default["LastSelectFolder"] = folderName;
                Properties.Settings.Default.Save();
            }
            else
            {
                ShowError($"Folder {basePath} not found.");
                cboPlayer.SelectedIndex = -1;
                LoadPlayerFolders();
            }
        }

        private void LoadPlayerFolders()
        {
            var saveFolders = Directory.GetDirectories(BasePath);
            PlayerFolders = saveFolders.Select(x => Path.GetFileName(x)).ToArray();
            cboPlayer.Items.AddRange(PlayerFolders);
        }

        private void LoadSaveTypes()
        {
            if (string.IsNullOrEmpty(PlayerPath)) { return; }

            var files = Directory.GetFiles(PlayerPath, "gamedata_*.ato");
            var saveFiles = files
                .Where(x => Regex.IsMatch(Path.GetFileName(x), "gamedata_\\d\\.ato"))
                .Select(x => Path.GetFileNameWithoutExtension(x)).ToArray();

            cboSaveType.Items.Clear();
            cboSaveType.Items.AddRange(saveFiles);
        }

        private void LoadSaveFilesByType()
        {
            var saveFilePrefix = cboSaveType.SelectedItem as string;
            if (string.IsNullOrEmpty(saveFilePrefix)) { return; }

            var saveFiles = Directory.GetFiles(PlayerPath, saveFilePrefix + "*");
            saveFiles = saveFiles.Where(x => x.EndsWith(".ato")).Select(x => Path.GetFileName(x)).ToArray();

            lstSaveFiles.Items.Clear();
            lstSaveFiles.Items.AddRange(saveFiles);
        }

        private void lstSaveFolder_DoubleClick(object sender, EventArgs e)
        {
            var folder = lstSaveFiles.SelectedItem?.ToString();
            if (!string.IsNullOrEmpty(folder))
            {
                Process.Start(Path.Combine(PlayerPath, folder));
            }
        }

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            UnhookWindowsHookEx(_hookID);
        }

        private void cboSaveTypes_SelectedIndexChanged(object sender, EventArgs e)
        {
            LoadSaveFilesByType();
        }

        private void lnkGitHub_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("https://github.com/catchtest/AcrossTheObeliskLoader");
        }

        private void picLogo_Click(object sender, EventArgs e)
        {
            Process.Start("https://store.steampowered.com/app/1385380/Across_the_Obelisk/");
        }
    }
}
