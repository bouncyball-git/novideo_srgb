using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;
using Microsoft.Win32;
using NvAPIWrapper.Display;

namespace novideo_srgb
{
    public class MainViewModel
    {
        public ObservableCollection<MonitorData> Monitors { get; }

        private string _configPath;

        private string _startupName;
        private RegistryKey _startupKey;
        private string _startupValue;

        public MainViewModel()
        {
            Logger.Log("MainViewModel ctor: start");
            Monitors = new ObservableCollection<MonitorData>();
            _configPath = AppDomain.CurrentDomain.BaseDirectory + "config.xml";
            Logger.Log("MainViewModel ctor: configPath=" + _configPath + " exists=" + File.Exists(_configPath));

            _startupName = "novideo_srgb";
            _startupKey = Registry.CurrentUser.OpenSubKey
                ("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
            Logger.Log("MainViewModel ctor: startup registry key opened ok=" + (_startupKey != null));
            _startupValue = Application.ExecutablePath + " -minimize";

            UpdateMonitors();
            Logger.Log("MainViewModel ctor: done, monitor count=" + Monitors.Count);
        }

        public bool? RunAtStartup
        {
            get
            {
                var keyValue = _startupKey.GetValue(_startupName);

                if (keyValue == null)
                {
                    return false;
                }

                if ((string)keyValue == _startupValue)
                {
                    return true;
                }

                return null;
            }
            set
            {
                if (value == true)
                {
                    _startupKey.SetValue(_startupName, _startupValue);
                }
                else
                {
                    _startupKey.DeleteValue(_startupName);
                }
            }
        }

        private void UpdateMonitors()
        {
            Logger.Log("UpdateMonitors: start");
            Monitors.Clear();
            List<XElement> config = null;
            if (File.Exists(_configPath))
            {
                try
                {
                    config = XElement.Load(_configPath).Descendants("monitor").ToList();
                    Logger.Log("UpdateMonitors: config.xml loaded, " + config.Count + " entries");
                }
                catch (Exception ex)
                {
                    Logger.LogException("UpdateMonitors: failed to load config.xml", ex);
                    throw;
                }
            }
            else
            {
                Logger.Log("UpdateMonitors: no config.xml, starting fresh");
            }

            HashSet<string> hdrPaths;
            try
            {
                hdrPaths = DisplayConfigManager.GetHdrDisplayPaths();
                Logger.Log("UpdateMonitors: HDR paths discovered, count=" + hdrPaths.Count);
            }
            catch (Exception ex)
            {
                Logger.LogException("UpdateMonitors: GetHdrDisplayPaths threw", ex);
                throw;
            }

            Display[] nvDisplays;
            try
            {
                nvDisplays = Display.GetDisplays().ToArray();
                Logger.Log("UpdateMonitors: NvAPI Display.GetDisplays returned " + nvDisplays.Length + " display(s)");
            }
            catch (Exception ex)
            {
                Logger.LogException("UpdateMonitors: NvAPI Display.GetDisplays threw", ex);
                throw;
            }

            WindowsDisplayAPI.Display[] winDisplays;
            try
            {
                winDisplays = WindowsDisplayAPI.Display.GetDisplays().ToArray();
                Logger.Log("UpdateMonitors: WindowsDisplayAPI.Display.GetDisplays returned " + winDisplays.Length + " display(s)");
                foreach (var wd in winDisplays)
                {
                    Logger.Log("  win display: name='" + wd.DisplayName + "' devicePath='" + wd.DevicePath + "'");
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("UpdateMonitors: WindowsDisplayAPI.Display.GetDisplays threw", ex);
                throw;
            }

            var number = 1;
            foreach (var display in nvDisplays)
            {
                Logger.Log("UpdateMonitors: processing nv display name='" + display.Name + "'");
                var match = winDisplays.FirstOrDefault(x => x.DisplayName == display.Name);
                if (match == null)
                {
                    Logger.Log("UpdateMonitors: NO MATCH in winDisplays for nv display '" + display.Name + "', skipping");
                    continue;
                }
                var path = match.DevicePath;
                Logger.Log("UpdateMonitors: matched, devicePath='" + path + "'");

                var hdrActive = hdrPaths.Contains(path);
                Logger.Log("UpdateMonitors: hdrActive=" + hdrActive);

                var settings = config?.FirstOrDefault(x => (string)x.Attribute("path") == path);
                Logger.Log("UpdateMonitors: config entry found=" + (settings != null));

                MonitorData monitor;
                try
                {
                    if (settings != null)
                    {
                        monitor = new MonitorData(this, number++, display, path, hdrActive,
                            (bool)settings.Attribute("clamp_sdr"),
                            (bool)settings.Attribute("use_icc"),
                            (string)settings.Attribute("icc_path"),
                            (bool)settings.Attribute("calibrate_gamma"),
                            (int)settings.Attribute("selected_gamma"),
                            (double)settings.Attribute("custom_gamma"),
                            (double)settings.Attribute("custom_percentage"),
                            (int)settings.Attribute("target"),
                            (bool)settings.Attribute("disable_optimization"));
                    }
                    else
                    {
                        monitor = new MonitorData(this, number++, display, path, hdrActive, false);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogException("UpdateMonitors: MonitorData ctor threw for '" + display.Name + "'", ex);
                    throw;
                }

                Monitors.Add(monitor);
                Logger.Log("UpdateMonitors: MonitorData added (#" + monitor.Number + " '" + monitor.Name + "')");
            }

            Logger.Log("UpdateMonitors: applying initial clamp on " + Monitors.Count + " monitor(s)");
            foreach (var monitor in Monitors)
            {
                try
                {
                    monitor.ReapplyClamp();
                }
                catch (Exception ex)
                {
                    Logger.LogException("UpdateMonitors: ReapplyClamp threw for #" + monitor.Number, ex);
                    throw;
                }
            }
            Logger.Log("UpdateMonitors: done");
        }

        public void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            UpdateMonitors();
        }

        public void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume) return;
            OnDisplaySettingsChanged(null, null);
        }

        public void SaveConfig()
        {
            try
            {
                var xElem = new XElement("monitors",
                    Monitors.Select(x =>
                        new XElement("monitor", new XAttribute("path", x.Path),
                            new XAttribute("clamp_sdr", x.ClampSdr),
                            new XAttribute("use_icc", x.UseIcc),
                            new XAttribute("icc_path", x.ProfilePath),
                            new XAttribute("calibrate_gamma", x.CalibrateGamma),
                            new XAttribute("selected_gamma", x.SelectedGamma),
                            new XAttribute("custom_gamma", x.CustomGamma),
                            new XAttribute("custom_percentage", x.CustomPercentage),
                            new XAttribute("target", x.Target),
                            new XAttribute("disable_optimization", x.DisableOptimization))));
                xElem.Save(_configPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message + "\n\nTry extracting the program elsewhere.");
                Environment.Exit(1);
            }
        }
    }
}