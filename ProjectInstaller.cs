/**
 * Copyright 2022 Casey Diemel
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation 
 * files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, 
 * modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the 
 * Software is furnished to do so.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES 
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS 
 * BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF 
 * OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */
using Microsoft.Win32;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration.Install;
using System.Diagnostics;
using System.ServiceProcess;

namespace WMIFileMonitorService
{
	/// <summary>
	/// Summary description for ProjectInstaller.
	/// </summary>
	[RunInstaller(true)]
	public class ProjectInstaller : System.Configuration.Install.Installer
	{
		private System.ServiceProcess.ServiceProcessInstaller serviceProcessInstaller1;
		private System.ServiceProcess.ServiceInstaller serviceInstaller1;
		private String SourceName;
		/// <summary>
		/// Required designer variable.
		/// </summary>

		public ProjectInstaller()
		{
			// This call is required by the Designer.
			InitializeComponent();

			// TODO: Add any initialization after the InitComponent call
		}

		#region Component Designer generated code
		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.serviceProcessInstaller1 = new System.ServiceProcess.ServiceProcessInstaller();
			this.serviceInstaller1 = new System.ServiceProcess.ServiceInstaller();
			// 
			// serviceProcessInstaller1
			// 
			this.serviceProcessInstaller1.Account = System.ServiceProcess.ServiceAccount.LocalSystem;
			this.serviceProcessInstaller1.Password = null;
			this.serviceProcessInstaller1.Username = null;
			// 
			// serviceInstaller1
			// 
			this.serviceInstaller1.ServiceName = FilesystemMonitor.ServiceName;
			this.serviceInstaller1.DisplayName = FilesystemMonitor.DisplayName;
			this.SourceName = FilesystemMonitor.SourceName; ; // Source the EventLogs come from
			this.serviceInstaller1.StartType = System.ServiceProcess.ServiceStartMode.Automatic;
			// 
			// ProjectInstaller
			// 
			this.Installers.AddRange(new System.Configuration.Install.Installer[] {
				this.serviceProcessInstaller1,
				this.serviceInstaller1
			});

		}
		#endregion
		private void serviceProcessInstaller_AfterInstall(object sender, System.Configuration.Install.InstallEventArgs e)
		{
			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine("[+] Starting service");
			Console.ResetColor();
			// Auto-start service after install
			ServiceInstaller serviceInstaller = (ServiceInstaller)sender;
			using (ServiceController sc = new ServiceController(serviceInstaller.ServiceName))
			{
				sc.Start();
			}
		}
		public override void Install(IDictionary stateServer)
		{
			List<string> logs = new List<string>();
			logs.Add($"{this.serviceInstaller1.ServiceName} Install Log\n-----------------------------------");

			//  HKEY_LOCAL_MACHINE\Services\CurrentControlSet\Services
			RegistryKey key_service;
			//  \<service_name> - service-specific folder
			RegistryKey key_app;
			//  \Parameters - service-specific configuration
			RegistryKey key_config;
			//  HKEY_LOCAL_MACHINE\Services\CurrentControlSet\Services\EventLog - Service specific Event logs
			//RegistryKey key_eventLog;
			//  HKEY_LOCAL_MACHINE\Services\CurrentControlSet\Services\EventLog<app_log_name> - Service specific Event logs
			//RegistryKey key_eventLog_app;

			try
			{
				//Let the project installer do its job
				base.Install(stateServer);
				
				//Open the HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\services key and allow writing
				key_service = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services", false);

				/***
				 * Create Event Log Keys
				 ***/
				try
				{
					//EventLog.DeleteEventSource(this.serviceInstaller1.ServiceName);
					if (EventLog.SourceExists(this.serviceInstaller1.DisplayName)) { EventLog.DeleteEventSource(this.serviceInstaller1.DisplayName); }
					if (EventLog.SourceExists("BCA Monitor")) { EventLog.DeleteEventSource("BCA Monitor"); }
					logs.Add($"[+] Deleted Event Sources");
				}
				catch (Exception e)
				{
					Console.WriteLine($"[!] Unable to removed default event logs.\n\n{e.ToString()}");
					logs.Add($"[!] Unable to removed default event logs.\n\n{e.ToString()}");
				}
				try
				{
					if(!EventLog.SourceExists("BCA Monitor"))
					{
						EventLog.CreateEventSource(this.SourceName, this.serviceInstaller1.DisplayName);
						Console.ForegroundColor = ConsoleColor.Green;
						Console.WriteLine("[+] Successfully created new event log.");
						Console.ResetColor();
						logs.Add($"[+] Successfully created new event log.");
					} 
					else
					{
						Console.ForegroundColor = ConsoleColor.Yellow;
						Console.WriteLine("[*] Event log already exists.");
						Console.ResetColor();
						logs.Add($"[*] Event log already exists.");
					}

				}
				catch (Exception e)
				{
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine($"[!] Unable to create new event log.\n\n{e.ToString()}");
					Console.ResetColor();
					logs.Add($"[!] Unable to create new event log.\n\n{e.ToString()}");
				}

				/***
				 * Create Default Configuration
				 ***/
				// open \<service_name>
				key_app = key_service.OpenSubKey(this.serviceInstaller1.ServiceName, true);
				//Service Description
				key_app.SetValue("Description", "BCA WMI Provider to monitor BCA devices. Monitors the following:\r\n- Failed Reports \r\n- Successful Reports \r\n- Drive Space", RegistryValueKind.String);
				//Create Parameters Registry folder
				key_config = key_app.CreateSubKey("Parameters");

				//** Development Values **
				//**
				// Initial folder value - Success
				key_config.SetValue("ProcessedDirectory", @"C:\Users\cdiemel\source\repos\BCA-WMI\BCAWMIService\TestFolder\Processed", RegistryValueKind.String);
				// Initial folder value - Failed
				key_config.SetValue("FailedDirectory", @"C:\Users\cdiemel\source\repos\BCA-WMI\BCAWMIService\TestFolder\Failed", RegistryValueKind.String);
				// Initial file value - Users File
				key_config.SetValue("UsersFilePath", @"C:\Users\cdiemel\source\repos\BCA-WMI\BCAWMIService\TestFolder\_usersfile.txt", RegistryValueKind.String);
				// Initial polling interval value - 1 min
				key_config.SetValue("PollingInterval", 1, RegistryValueKind.DWord);
				// Initial users file MaxAge - 5 minutes
				key_config.SetValue("UsersFileMaxAge", 5, RegistryValueKind.DWord);
				// Initial logging level - 2
				key_config.SetValue("LoggingLevel", 2, RegistryValueKind.DWord);
				// Service name for Epic BCA Client Service - EpicBCAClient102
				key_config.SetValue("BCAClientProcessName", "PlugPlay", RegistryValueKind.String);
				// Service name for Epic Print Service - EpicPrintService102
				key_config.SetValue("EpicPrintServiceProcessName", "Spooler", RegistryValueKind.String);
				/*/

				//** Production Values **

				// Initial folder value - Success
				key_config.SetValue("ProcessedDirectory", @"C:\Epic\Jobs\Processed", RegistryValueKind.String);
				// Initial folder value - Failed
				key_config.SetValue("FailedDirectory", @"C:\Epic\Jobs\Failed",RegistryValueKind.String);
				// Initial file value - Users File
				key_config.SetValue("UsersFilePath", @"C:\Epic\Data\Epic BCA Client\_users_.emp", RegistryValueKind.String);
				// Initial polling interval value - 10 min
				key_config.SetValue("PollingInterval", 10, RegistryValueKind.DWord);
				// Initial users file MaxAge - 1445 minutes (1 day)
				key_config.SetValue("UsersFileMaxAge", 1445, RegistryValueKind.DWord);
				// Initial logging level - 2
				key_config.SetValue("LoggingLevel", 2, RegistryValueKind.DWord);
				// Service name for Epic BCA Client Service - EpicBCAClient102
				key_config.SetValue("BCAClientProcessName", "EpicBCAClient102", RegistryValueKind.String);
				// Service name for Epic Print Service - EpicPrintService102
				key_config.SetValue("EpicPrintServiceProcessName", "EpicPrintService102", RegistryValueKind.String);
				//**/

				Console.ForegroundColor = ConsoleColor.Green;
				Console.WriteLine("[+] Successfully created default configuration Registry Keys.");
				Console.ResetColor();
				logs.Add($"[+] Successfully created default configuration Registry Keys.");

				/***
				 * Create New Event Log
				 ***/
				//EventLog eventLog = new EventLog();
				//eventLog.Log = "BCA Monitor";

			}
			catch (Exception e)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"[!!] An exception was thrown during service installation: {e.ToString()}");
				Console.ResetColor();
				EventLog.WriteEntry("BCA Monitor", $"[!!] An exception was thrown during service installation: {e.ToString()}", EventLogEntryType.Error, 50);
				logs.Add($"[!!] An exception was thrown during service installation: {e.ToString()}");
			}
			logs.Add($"[*] Starting BCA Monitor service.");
			EventLog.WriteEntry("BCA Monitor", String.Join("\n",logs.ToArray()), EventLogEntryType.Information, 0, 1);

			try
			{
				Console.ForegroundColor = ConsoleColor.Yellow;
				ServiceController sc = new ServiceController(this.serviceInstaller1.DisplayName);
				Console.WriteLine($"[i] {sc.ToString()}");
				Console.WriteLine($"[+] Status: {sc.Status}");
				Console.WriteLine($"[+] ServiceName: {sc.ServiceName}");
				Console.WriteLine($"[+] DisplayName: {sc.DisplayName}");
				sc.Start();
				sc.Refresh();
				Console.WriteLine($"Status: {sc.Status}");
				sc.WaitForStatus(ServiceControllerStatus.Running);
				Console.ForegroundColor = ConsoleColor.Green;
				Console.WriteLine($"[+] Service {this.serviceInstaller1.ServiceName} started.");
				Console.ResetColor();
			}
			catch (Exception e)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"[!] Unable to start service {this.serviceInstaller1.ServiceName}\n\nError:{e.GetType()}\n\nMsg: {e.Message}\n\nStacktrace:\n{e.StackTrace}");
				Console.ResetColor();
				EventLog.WriteEntry("BCA Monitor", $"[!] Unable to start service {this.serviceInstaller1.ServiceName}.\n\n{e.Message}\n\nStacktrace:\n{e.StackTrace}", EventLogEntryType.Error, 50);
			}
		}

		public override void Uninstall(IDictionary stateServer)
		{
			List<string> logs = new List<string>();
			logs.Add($"{this.serviceInstaller1.DisplayName} Uninstall Log\n---------------------------------------");
			ServiceController sc = new ServiceController(this.serviceInstaller1.ServiceName);
			Console.WriteLine(sc.Status.ToString());
			if(sc.Status != ServiceControllerStatus.Stopped)
            {
				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write($"[+] Stopping {this.serviceInstaller1.ServiceName} service");
				sc.Stop();
				int i = 0;
				Console.ForegroundColor = ConsoleColor.Red;
				sc.Refresh();
				while ((sc.Status != ServiceControllerStatus.Stopped))
                {
                    if (i > 10) { break; }
					Console.Write(".");
					sc.Refresh();
					System.Threading.Thread.Sleep(50);
					i++;
				}
				Console.WriteLine();
				Console.ResetColor();

				if (sc.Status != ServiceControllerStatus.Stopped)
				{
					logs.Add($"[!!!!] Unable to stop {this.serviceInstaller1.DisplayName} service.");
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine($"\n\n[!!!!] Unable to stop {this.serviceInstaller1.DisplayName} service.\n");
					Console.WriteLine($"\n[!!!!] Stop service {this.serviceInstaller1.DisplayName} before uninstalling.\n");
					Console.WriteLine($"\n** STOPPING INSTALL PROCESS  **\n\n");
					Console.ResetColor();
					return;
				}
				logs.Add($"[+] {this.serviceInstaller1.DisplayName} service stopped");
				Console.ForegroundColor = ConsoleColor.Green;
				Console.WriteLine($"[+] {this.serviceInstaller1.DisplayName} service stopped");
				Console.ResetColor();
			}

			try
			{
				//if (EventLog.SourceExists(this.serviceInstaller1.ServiceName)) { EventLog.DeleteEventSource(this.serviceInstaller1.ServiceName); }
				//if (EventLog.SourceExists("BCAMonitor")) { EventLog.DeleteEventSource("BCAMonitor"); }
				//if (EventLog.SourceExists("BCA Monitor")) { EventLog.DeleteEventSource("BCA Monitor"); }
				if (EventLog.SourceExists(this.SourceName)) { EventLog.DeleteEventSource(this.SourceName); }
				if (EventLog.Exists(this.serviceInstaller1.DisplayName)) { EventLog.Delete(this.serviceInstaller1.DisplayName); }
				Console.ForegroundColor = ConsoleColor.Green;
				Console.WriteLine($"[+] Successfully removed Event Sources.");
				Console.ResetColor();
				logs.Add($"[+] Successfully removed Event Sources.");
			}
			catch (Exception e)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"[!] unable to removed default event logs.\n{e.ToString()}");
				Console.ResetColor();
				logs.Add($"[!] unable to removed default event logs.\n{e.ToString()}");
			}

			RegistryKey services;
			RegistryKey service;
			RegistryKey eventlog;
			//RegistryKey eventLog;

			//Drill down to the service key and open it with write permission
			//Open the HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\services key and allow writing
			services = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services", false);

			try
			{
				service = services.OpenSubKey(this.serviceInstaller1.ServiceName, true);
				service.DeleteSubKeyTree("Parameters");

				eventlog = services.OpenSubKey("EventLog", true);
				eventlog.DeleteSubKeyTree("BCAMonitor");

				Console.ForegroundColor = ConsoleColor.Green;
				Console.WriteLine("[+] Successfully removed config Registry Keys.");
				Console.ResetColor();
				logs.Add($"[+] Successfully removed config Registry Keys.");
			}
			catch (Exception e)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine("[!] Exception encountered while removing config Registry Keys:\n" + e.ToString());
				Console.ResetColor();
				logs.Add($"[!] Exception encountered while removing config Registry Keys:\n{e.ToString()}");
			}


			//Let the project installer do its job
			base.Uninstall(stateServer);

			// make list here and turn into a loop
			List<string> list_rmLogs = new List<string>();
			list_rmLogs.Add("BCAMonitor");
			list_rmLogs.Add("BCA Monitor");
			//list_rmLogs.Add("TechOps");
			foreach (string str_rmLogName in list_rmLogs)
			{
				if (EventLog.SourceExists(str_rmLogName))
				{
					string str_srcLog = EventLog.LogNameFromSourceName(str_rmLogName, ".");
					Console.ForegroundColor = ConsoleColor.Red;
					//Console.WriteLine($"[!!] Exists:\n - Log:{str_rmLogName}\n - Source:{str_srcLog}");
					Console.WriteLine($"[!!] Exists:\n - Source:{str_srcLog}");
					Console.ResetColor();
					//logs.Add($"[!!] Exists:\n - Log:{str_rmLogName}\n - Source:{str_srcLog}");
					logs.Add($"[!!] Exists:\n - Source:{str_srcLog}");
					//EventLog.Delete(str_rmLogName);
					EventLog.DeleteEventSource(str_srcLog);
					continue;
				}
			}

			EventLog.WriteEntry("BCA Monitor", $"{String.Join("\n",logs.ToArray())}", EventLogEntryType.Information, 50);
		}
	}
	
}
