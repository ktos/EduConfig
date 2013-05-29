﻿/**
 * 
 * EduConfig, wersja 1.0
 * Copyright (C) Politechnika Lubelska 2013, Marcin Badurowicz <m.badurowicz at pollub dot pl>
 * 
 * Aplikacja umożliwiająca automatyczną konfigurację profilu sieci bezprzewodowej eduroam.
 *
 * EduConfig is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * EduConfig is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with EduConfig. If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using Streaia;
using System.Reflection;

namespace Pollub.EduConfig
{
    /// <summary>
    /// Możliwe kody wyjścia programu w postaci listy flag
    /// </summary>
    [Flags]
    enum ExitCode : int
    {
        NoError = 0,
        CertInstallError = 1,
        ProfileInstallError = 2,
        SystemNotSupported = 4,
        NoAdmin = 8,
        UnhandledException = 16
    }

    class Program
    {
        private static ParamParser pp;

        static void Main(string[] args)
        {
            bool silentMode = false;

            try
            {
                pp = new ParamParser(Environment.GetCommandLineArgs());
                pp.AddExpanded("/s", "/silent");
                pp.AddExpanded("/?", "--help");

                pp.Parse();
                silentMode = pp.SwitchExists("/silent");

                if (pp.VersionRequested())
                {
                    ShowVersion();
                    Exit(ExitCode.NoError);
                }

                if (pp.HelpRequested())
                    ShowHelp();

                if (!IsRunningAsAdministrator())
                {
                    var selfName = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                    ProcessStartInfo self = new ProcessStartInfo(selfName);
                    self.Verb = "runas";
                    self.WindowStyle = ProcessWindowStyle.Hidden;
                    if (silentMode) self.Arguments = "/silent";

                    try
                    {
                        System.Diagnostics.Process.Start(self);
                    }
                    catch (System.ComponentModel.Win32Exception ex)
                    {
                        if (!silentMode)
                            MessageBox.Show(AppResources.NeedAdmin, AppResources.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);

                        Exit(ExitCode.NoAdmin);
                    }

                    Exit(ExitCode.NoError);
                }

                if ((!silentMode) && (!IsSystemSupported()))
                {                    
                    if (MessageBox.Show(AppResources.SystemNotSupported, AppResources.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
                        Exit(ExitCode.SystemNotSupported);                    
                }

                if ((silentMode) || (MessageBox.Show(AppResources.Info, AppResources.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes))
                {
                    var exc = ExitCode.NoError;
                    var certResult = InstallCACertificate();
                    if (certResult != 0)
                    {
                        if (!silentMode)
                            MessageBox.Show(AppResources.CertFail + " " + certResult, AppResources.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        exc = ExitCode.CertInstallError;
                    }

                    var netshResult = InstallProfile();
                    if (netshResult != 0)
                    {
                        if (!silentMode)
                            MessageBox.Show(AppResources.ProfFail + " " + netshResult, AppResources.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        exc = exc | ExitCode.ProfileInstallError;
                    }

                    if (exc == ExitCode.NoError)
                        if (!silentMode)
                            MessageBox.Show(AppResources.Success, AppResources.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);

                    Exit(exc);
                }

                Exit(ExitCode.NoError);
            }
            catch (Exception ex)
            {
                if (!silentMode)
                    MessageBox.Show(AppResources.UnhandledException + " " + ex.Message, AppResources.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                else
                    Console.Error.WriteLine(AppResources.UnhandledException + " " + ex.Message);

                Exit(ExitCode.UnhandledException);
            }

        }

        /// <summary>
        /// Pokazuje informacje o wersji
        /// </summary>
        private static void ShowVersion()
        {
            Console.WriteLine(AppResources.AppName + " " + Assembly.GetExecutingAssembly().GetName().Version);
            Console.WriteLine(AppResources.Copyright);
        }

        /// <summary>
        /// Pokazuje pomoc
        /// </summary>
        private static void ShowHelp()
        {
            ShowVersion();
            Console.WriteLine();
            Console.WriteLine(AppResources.Help);
            Exit(ExitCode.NoError);
        }

        /// <summary>
        /// Kończy działanie aplikacji z określonym kodem błędu
        /// </summary>
        /// <param name="e"></param>
        private static void Exit(ExitCode e)
        {
            Environment.Exit((int)e);
        }

        /// <summary>
        /// Instaluje certyfikat w Trusted Root CA
        /// </summary>
        private static int InstallCACertificate()
        {
            var cert = Path.GetTempFileName().Replace(".tmp", ".der");
            File.WriteAllBytes(cert, AppResources.plca_cert);

            ProcessStartInfo p = new ProcessStartInfo("certutil", String.Format("-addstore Root \"{0}\"", cert));
            p.Verb = "runas";
            p.WindowStyle = ProcessWindowStyle.Hidden;
            var certutil = Process.Start(p);

            // oczekiwanie na zakończenie certutila
            while (!certutil.HasExited) ;

            File.Delete(cert);

            return certutil.ExitCode;

        }

        /// <summary>
        /// Instaluje profil sieciowy na podstawie pliku XML
        /// </summary>
        private static int InstallProfile()
        {
            var prof = Path.GetTempFileName().Replace(".tmp", ".xml");
            File.WriteAllText(prof, AppResources.wlan0_eduroam);

            ProcessStartInfo p = new ProcessStartInfo("netsh", String.Format("wlan add profile filename=\"{0}\" user=all", prof));
            p.Verb = "runas";
            p.WindowStyle = ProcessWindowStyle.Hidden;
            var netsh = Process.Start(p);

            // oczekiwanie na zakończenie działania netsh
            while (!netsh.HasExited) ;

            File.Delete(prof);

            return netsh.ExitCode;
        }

        /// <summary>
        /// Sprawdza czy aplikacja jest uruchomiona jako administrator
        /// </summary>
        /// <returns></returns>
        private static bool IsRunningAsAdministrator()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        /// <summary>
        /// Sprawdza czy system jest obsługiwany oficjalnie
        /// </summary>
        /// <returns></returns>
        private static bool IsSystemSupported()
        {
            return (Environment.OSVersion.Platform == PlatformID.Win32NT) && (Environment.OSVersion.Version.Major >= 6);
        }
    }
}
