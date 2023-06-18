/**
 * Copyright 2023 Casey Diemel
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Instrumentation;
using System.Runtime.Caching;
using System.ServiceProcess;
using System.Text;
// using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace WMIFileMonitorService
{
	public class FilesystemMonitor
	{
		public const String ServiceName = "BCAMonitor"; // Also Log name
		public const String DisplayName = "BCA Monitor";
		public const String SourceName = "BCA Monitor";// Source the EventLogs come from

		/// <summary> Name of the Windows service. </summary>
		private System.ComponentModel.Container components = null;
		/// <summary> <see cref="Win32_PerfFormattedData_BCAMonitor"/> instance to interface with Windows Managment Instrumentation Provider. </summary>
		readonly Win32_PerfFormattedData_BCAMonitor wmiProvider;
		/// <summary> <see cref="EventLogger"/> instance for logging messages to Windows Event Viewer. </summary>
		private readonly EventLogger logger;
		/// <summary> Class level <see cref="List{T}"/> of log messages that need to be output. </summary>
		private List<string> list_compoundLog = new List<string>(6);
		/// <summary> <see cref="Dictionary{TKey, TValue}"/> of configuration values objects. </summary>
		private Dictionary<string, string> dict_config;
		/// <summary> <see cref="Dictionary{TKey, TValue}"/> of FileSystemWatcher objects. </summary>
		private Dictionary<string, FileSystemWatcher> list_FileWatcher = new Dictionary<string, FileSystemWatcher>(3);
		/// <summary>  Interval poller <see cref="Timer"/>. </summary>
		private Timer timer_IntervalPoller = new Timer();
		/// <summary> <see cref="byte"/> (0-255) value of number of alerts that have been logged for UsersFile errors. </summary>
		private byte byte_UsersFileAlerts = 0;
		/// <summary> <see cref="Boolean"/> value that determins if memory allocation is logged. </summary>
		private bool bool_MonitorMemory = false;

		// for memcaching UpdateFolder/UpdateFile calls so we dont do it 1000 times per second
		/// <summary> Cache time to hold events before processing. </summary>
		private const int _CacheTimeMS = 1000;
		/// <summary> Directory <see cref="MemoryCache"/> for holding generated event objects. </summary>
		private readonly MemoryCache _DirMemCache;
		/// <summary> File <see cref="MemoryCache"/> for holding generated event objects. </summary>
		private readonly MemoryCache _FileMemCache;
		/// <summary> Directory <see cref="CacheItemPolicy"/> for generated event objects. </summary>
		private readonly CacheItemPolicy _DirCacheItemPolicy;
		/// <summary> File <see cref="CacheItemPolicy"/> for generated event objects. </summary>
		private readonly CacheItemPolicy _FileCacheItemPolicy;

		/// <summary>
		/// Contstructor for <see cref="MonitorService"/>
		/// </summary>
		/// <param name="eventLogger"><see cref="EventLogger"/> instance</param>
		public FilesystemMonitor(EventLogger eventLogger)
		{
			// Configure event logger
			this.logger = eventLogger;
			this.logger.LogEvent("In MonitorService Constructor", EventLogger.LogID.MethodStart);

			// If we have an event logger, add a note to the event log list
			if (this.logger is EventLogger)
			{
				this.list_compoundLog.Add("Event Logger\n--------------");
				this.list_compoundLog.Add($"[ i ] Log Level: {this.logger.getDebugLvl()}");
				this.list_compoundLog.Add("[+] File Monitor event logger loaded.");
				this.logger.LogList(this.list_compoundLog, EventLogger.LogID.EventLogger);
				//GC Stuff
				this.list_compoundLog.Clear();
				this.list_compoundLog.TrimExcess();
			}

			this.InitializeComponent();

			// Create WMI Provider instance
			this.wmiProvider = new Win32_PerfFormattedData_BCAMonitor();
			// Makes an instance visible through management instrumentation.
			Instrumentation.Publish(this.wmiProvider);

			// Load configuration from Windows Registry
			this.LoadConfig();

			// Load configuration from Windows Registry
			if (this.logger.getDebugLvl() > 4)
			{
				this.bool_MonitorMemory = true;
			}

			// Setup Directory MemoryCache
			this._DirMemCache = MemoryCache.Default;
			_DirCacheItemPolicy = new CacheItemPolicy()
			{
				RemovedCallback = this._DirectoryUpdateCache
			};

			// Setup File MemoryCache
			this._FileMemCache = MemoryCache.Default;
			_FileCacheItemPolicy = new CacheItemPolicy()
			{
				RemovedCallback = this._FileUpdateCache
			};


			this.AttachPollers();


		}
		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		static void Main()
        {
			// Configure event logger
			EventLogger logger = new EventLogger(FilesystemMonitor.SourceName);

			// Get logging level before we start
			short LogLevel = 2;
			RegistryKey parameters = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{MonitorService.str_ServiceName}\Parameters", false);
			if (parameters != null)
			{
				short.TryParse(parameters.GetValue("LoggingLevel", 3).ToString(), out LogLevel);
			}

			// set debug level
			logger.setDebug(LogLevel);

			// Log the license (MIT - No Attribution)
			logger.LogEvent($"{String.Join("", MonitorService.LICENSE)}", EventLogger.LogID.LICENSE, EventLogger.INFO);

			// Throws an INFO, WARN, ERROR, and TRACE event
			if (LogLevel > 2) { logger.testEvents(); }

			// Log event that the service is starting
			logger.LogEvent($"Starting {MonitorService.str_ServiceName} service.", EventLogger.LogID.ServiceStart, EventLogger.INFO);

			ServiceBase[] ServicesToRun;
			ServicesToRun = new ServiceBase[]
			{
				new MonitorService(logger)
            };
            ServiceBase.Run(ServicesToRun);
        }
		private void InitializeComponent()
		{
			components = new System.ComponentModel.Container();
		}
		/// <summary>
		/// Load configuration values from Windows Registry
		/// </summary>
		private void LoadConfig()
		{
			this.logger.LogEvent("MonitorService.LoadConfig", EventLogger.LogID.MethodStart);
			List<string> list_LoadConfigLog = new List<string>(6); //4
			list_LoadConfigLog.Add("Configuration\n----------------");
			// Create Config dictionary
			this.dict_config = new Dictionary<string, string>();

			// Open Registry Key parameters and set write to false
			RegistryKey parameters = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{MonitorService.str_ServiceName}\Parameters", false);
			Console.WriteLine(MonitorService.str_ServiceName);
			Console.WriteLine(parameters);
			String[] str_regKeyNames = parameters.GetValueNames();

			foreach (string keyName in str_regKeyNames)
			{
				// Log parameters as they are read
				this.logger.LogEvent($"[ i ] {keyName}={parameters.GetValue(keyName, "").ToString()}", EventLogger.LogID.MSConfigDbg);
				list_LoadConfigLog.Add($"[ i ] {keyName} = {parameters.GetValue(keyName, "").ToString()}");
				try
				{
					this.dict_config.Add(keyName, parameters.GetValue(keyName, "").ToString());
					this.logger.LogEvent($"{keyName} added.", EventLogger.LogID.MSConfigDbg);
					list_LoadConfigLog.Add($"    - added.");
				}
				catch (Exception e_key)
				{
					this.logger.LogTrace(e_key, $"Exception on key {keyName}");
					list_LoadConfigLog.Add($"Exception on key {keyName}");
				}
			}
			this.logger.LogEvent("[+] Configuration loaded", EventLogger.LogID.MSConfigDbg);
			list_LoadConfigLog.Add("[+] Configuration loaded\n");
			this.logger.LogList(list_LoadConfigLog, EventLogger.LogID.MSConfig);
			//GC Stuff
			list_LoadConfigLog = null;
			this.VerifyConfig();
		}
		/// <summary>
		/// Normalize configuration values and ensure they are useable/exist/correct
		/// </summary>
		private void VerifyConfig()
		{
			this.logger.LogEvent("MonitorService.VerifyConfig", EventLogger.LogID.MethodStart);
			List<string> list_VerifyConfigLog = new List<string>(25); //23
			list_VerifyConfigLog.Add("Config Verfication\n----------------");
			// Verify Processed Directory
			try
			{
				list_VerifyConfigLog.Add("[ i ] ProcessedDirectory");
				this.logger.LogEvent("[ i ] ProcessedDirectory", EventLogger.LogID.MSConfigVerifyDbg, EventLogger.INFO);
				String str_path = this.dict_config["ProcessedDirectory"].ToString();
				if (str_path[str_path.Length - 1] != Path.DirectorySeparatorChar)
				{
					this.dict_config["ProcessedDirectory"] += Path.DirectorySeparatorChar.ToString();
				}
				list_VerifyConfigLog.Add($"     - {(string)this.dict_config["ProcessedDirectory"]}");
				this.logger.LogEvent($"     - {(string)this.dict_config["ProcessedDirectory"]}", EventLogger.LogID.MSConfigVerifyDbg, EventLogger.INFO);
				Directory.GetDirectories((string)this.dict_config["ProcessedDirectory"]);
				this.dict_config["ProcessedDirectoryName"] = new DirectoryInfo(this.dict_config["ProcessedDirectory"]).Name;
			}
			catch (Exception e_dir)
			{
				this.dict_config.Remove("ProcessedDirectory");
				list_VerifyConfigLog.Add($"     [ ! ] Unable to add ProcessedDirectory");
				list_VerifyConfigLog.Add($"   - {e_dir.GetType().Name}");
				list_VerifyConfigLog.Add($"   - {e_dir.Message}");
				this.wmiProvider.ReportsProcessed = -1;
				this.logger.LogTrace(e_dir, "Unable to add ProcessedDirectory", EventLogger.LogID.MSConfigProcFail);
			}
			// Verify Failed Directory
			try
			{
				list_VerifyConfigLog.Add("[ i ] FailedDirectory");
				this.logger.LogEvent("[ i ] FailedDirectory", EventLogger.LogID.MSConfigVerifyDbg, EventLogger.INFO);
				String str_path = this.dict_config["FailedDirectory"].ToString();
				if (str_path[str_path.Length - 1] != Path.DirectorySeparatorChar)
				{
					this.dict_config["FailedDirectory"] += Path.DirectorySeparatorChar.ToString();
				}
				list_VerifyConfigLog.Add($"     - {(string)this.dict_config["FailedDirectory"]}");
				this.logger.LogEvent($"     - {(string)this.dict_config["FailedDirectory"]}", EventLogger.LogID.MSConfigVerifyDbg, EventLogger.INFO);
				Directory.GetDirectories((string)this.dict_config["FailedDirectory"]);
				this.dict_config["FailedDirectoryName"] = new DirectoryInfo(this.dict_config["FailedDirectory"]).Name;
			}
			catch (Exception e_dir)
			{
				this.dict_config.Remove("FailedDirectory");
				list_VerifyConfigLog.Add($"     [ ! ] Unable to add FailedDirectory");
				list_VerifyConfigLog.Add($"   - {e_dir.GetType().Name}");
				list_VerifyConfigLog.Add($"   - {e_dir.Message}");
				this.wmiProvider.ReportsFailed = -1;
				this.logger.LogTrace(e_dir, "Unable to add FailedDirectory", EventLogger.LogID.MSConfigFailedFail);
			}
			// Verify Users File
			try
			{
				list_VerifyConfigLog.Add("[ i ] UsersFilePath");
				this.logger.LogEvent("[ i ] UsersFilePath", EventLogger.LogID.MSConfigVerifyDbg, EventLogger.INFO);
				String str_path = this.dict_config["UsersFilePath"].ToString();
				list_VerifyConfigLog.Add($"     - {(string)this.dict_config["UsersFilePath"]}");
				this.logger.LogEvent($"     - {(string)this.dict_config["UsersFilePath"]}", EventLogger.LogID.MSConfigVerifyDbg, EventLogger.INFO);
				File.GetAttributes((string)this.dict_config["UsersFilePath"]);
			}
			catch (Exception e_dir)
			{
				this.dict_config.Remove("UsersFilePath");
				list_VerifyConfigLog.Add($"     [ ! ] Unable to add UsersFilePath");
				list_VerifyConfigLog.Add($"     - {e_dir.GetType().Name}");
				list_VerifyConfigLog.Add($"     - {e_dir.Message}");
				this.wmiProvider.UserUpdatedAge = 0;
				this.logger.LogTrace(e_dir, "Unable to add UsersFilePath", EventLogger.LogID.MSConfigUsersFail);
			}
			// Verify Polling Interval
			double interval = 10;
			try
			{
				list_VerifyConfigLog.Add("[ i ] PollingInterval");
				this.logger.LogEvent("[ i ] PollingInterval", EventLogger.LogID.MSConfigVerifyDbg, EventLogger.INFO);
				double.TryParse((string)this.dict_config["PollingInterval"], out interval);
				list_VerifyConfigLog.Add($"     - Parsed Iterval: {interval}min");
				if (interval == 0)
				{
					throw new ArgumentOutOfRangeException();
				}
				this.logger.LogEvent($"     - Final Interval: {interval}", EventLogger.LogID.MSConfigVerifyDbg, EventLogger.INFO);
			}
			catch (Exception e)
			{
				this.logger.LogTrace(e, $"     [ ! ] Config::PollingInterval must be > 0, defaulting to 10 minutes", EventLogger.LogID.MSConfigIntervalFail);
				list_VerifyConfigLog.Add($"     [ ! ] Config::PollingInterval must be > 0, defaulting to 10 minutes.\n Configured: {interval}min");
				this.logger.LogEvent($"     [ - ] Final Interval: {interval}", EventLogger.LogID.MSConfigVerifyDbg, EventLogger.INFO);
				this.dict_config["PollingInterval"] = "10";
			}

			/* Look into if this is going to be better memory wise */
			//ServiceController[] services = ServiceController.GetServices();
			//var scs = services.FirstOrDefault(s => s.ServiceName == this.dict_config["BCAClientProcessName"].ToString();
			//return scs != null;


			// Verfy  Client Service
			try
			{
				list_VerifyConfigLog.Add("[ i ] BCAClientProcessName");
				list_VerifyConfigLog.Add($"     - {this.dict_config["BCAClientProcessName"].ToString()}");
				this.logger.LogEvent("[ i ] BCAClientProcessName", EventLogger.LogID.MSConfigVerifyDbg, EventLogger.INFO);
				//GC Stuff
				using (ServiceController sc = new ServiceController(this.dict_config["BCAClientProcessName"].ToString())) { }
			}
			catch (Exception e_svc)
			{
				this.dict_config.Remove("BCAClientProcessName");
				list_VerifyConfigLog.Add($"     [ ! ] Unable to add BCAClientProcessName");
				list_VerifyConfigLog.Add($"     - {e_svc.GetType().Name}");
				list_VerifyConfigLog.Add($"     - {e_svc.Message}");
				this.logger.LogTrace(e_svc, "Unable to add BCAClientProcessName", EventLogger.LogID.MSConfigClientFail);
			}
			// Verfy  EPS Service
			try
			{
				list_VerifyConfigLog.Add("[ i ] EpicPrintServiceProcessName");
				list_VerifyConfigLog.Add($"     - {this.dict_config["EpicPrintServiceProcessName"].ToString()}");
				this.logger.LogEvent("[ i ] EpicPrintServiceProcessName", EventLogger.LogID.MSConfigVerifyDbg, EventLogger.INFO);
				//GC Stuff
				using (ServiceController sc = new ServiceController(this.dict_config["EpicPrintServiceProcessName"].ToString())) { }
			}
			catch (Exception e_svc)
			{
				this.dict_config.Remove("EpicPrintServiceProcessName");
				list_VerifyConfigLog.Add($"     [ ! ] Unable to add EpicPrintServiceProcessName");
				list_VerifyConfigLog.Add($"     [ - ] {e_svc.GetType().Name}");
				list_VerifyConfigLog.Add($"     [ - ] {e_svc.Message}");
				this.logger.LogTrace(e_svc, "Unable to add EpicPrintServiceProcessName", EventLogger.LogID.MSConfigEPSFail);
			}
			// Verfy Logging Level
			short.TryParse(this.dict_config["LoggingLevel"].ToString(), out short logLvl);
			if ((logLvl > 5 || logLvl < 1) && logLvl != 1337)
			{
				String msg = $"[!] Config::LoggingLevel must be between 1 and 4, defaulting to 2";
				this.logger.LogEvent(msg, EventLogger.LogID.MSConfigProcFail, EventLogger.WARN);
				list_VerifyConfigLog.Add(msg);
				this.dict_config["LoggingLevel"] = "2";
			}
			/*
			this.dict_config["UsersFileMaxAge"];
			*/
			this.logger.LogEvent("[+] Configuration verified", EventLogger.LogID.MSConfigDbg);
			list_VerifyConfigLog.Add("[+] Configuration verified");

			this.logger.LogList(list_VerifyConfigLog, EventLogger.LogID.MSConfigVerify);
			//GC Stuff
			list_VerifyConfigLog = null;
		}
		private void AttachPollers()
		{
			this.logger.LogEvent("MonitorService.AttachPollers", EventLogger.LogID.MethodStart);
			List<string> list_AttachPollersLog = new List<string>(10); //7
			list_AttachPollersLog.Add($"Attaching Pollers\n-----------------");

			double.TryParse((string)this.dict_config["PollingInterval"], out double interval);
			interval *= 60000; // milliseconds
			this.timer_IntervalPoller.Interval = interval;

			// Hook C Drive
			/** This is not needed due to WMI having the ability to pull all disks
			list_AttachPollersLog.Add($"[+] C Drive Space");
			this.HookIntervalEvent("C Drive", this.UpdateCDrive);
			this.UpdateCDrive();
			**/

			// Hook EPS Service
			if (this.dict_config.ContainsKey("EpicPrintServiceProcessName"))
			{
				list_AttachPollersLog.Add($"[+] EpicPrintServiceProcessName");
				this.HookIntervalEvent("EpicPrintServiceProcessName", this.UpdateService, "EpicPrintServiceProcessName");
				this.UpdateService("EpicPrintServiceProcessName");
			}

			// Hook  Client Service
			if (this.dict_config.ContainsKey("BCAClientProcessName"))
			{
				list_AttachPollersLog.Add($"[+] BCAClientProcessName");
				this.HookIntervalEvent("BCAClientProcessName", this.UpdateService, "BCAClientProcessName");
				this.UpdateService("BCAClientProcessName");
			}

			// Hook Processed Directory
			if (this.dict_config.ContainsKey("ProcessedDirectory"))
			{
				list_AttachPollersLog.Add($"[+] ProcessedDirectory");
				String path = this.dict_config["ProcessedDirectory"].ToString();
				this.HookFileSystemEvent("ProcessedDirectory", this.UpdateFolder, path);
				this.UpdateFolder(path);
			}

			// Hook Failed Directory
			if (this.dict_config.ContainsKey("FailedDirectory"))
			{
				list_AttachPollersLog.Add($"[+] FailedDirectory");
				String path = this.dict_config["FailedDirectory"].ToString();
				this.HookFileSystemEvent("FailedDirectory", this.UpdateFolder, path);
				this.UpdateFolder(path);
			}

			// Hook Users File
			if (this.dict_config.ContainsKey("UsersFilePath"))
			{
				list_AttachPollersLog.Add($"[+] UsersFilePath");
				String path = this.dict_config["UsersFilePath"].ToString();
				this.HookFileSystemEvent("UsersFilePath", this.UpdateFile, path);
				this.HookIntervalEvent("UsersFilePath", this.UpdateFile, path);
				this.UpdateFile(path);
			}

			// Run polling on allocated memory usage.
			if (this.bool_MonitorMemory)
			{
				list_AttachPollersLog.Add($"[+] Allocated Memory Usage");
				this.HookIntervalEvent("AllocatedMem", this.MonitorMemory);
				this.MonitorMemory();
			}

			// Start interval timer
			this.timer_IntervalPoller.Start();
			this.logger.LogList(list_AttachPollersLog, EventLogger.LogID.MSAttachPoller);
			//GC Stuff
			list_AttachPollersLog = null;
		}
		/// <summary>
		/// Add hook with <see cref="FileSystemWatcher" /> for a file using <see cref="FileWatcher" />.
		/// </summary>
		/// <param name="name">Name of the interval event</param>
		/// <param name="method">Function reference.</param>
		/// <param name="param">Parameter to pass to the <c>method</c></param>
		private void HookFileSystemEvent(String name, Action<String> method, String path = null)
		{
			this.logger.LogEvent("MonitorService.HookFileSystemEvent", EventLogger.LogID.MethodStart);
			List<string> list_hookFSLog = new List<string>(5); //4
			list_hookFSLog.Add($"Hooking FS Event\n-----------------");
			list_hookFSLog.Add($"[i] Name: {name}");
			list_hookFSLog.Add($"[i] Path: {path}");
			if (path is null)
			{
				list_hookFSLog.Add($"[!] Name is null!!");
				this.logger.LogList(list_hookFSLog, EventLogger.LogID.MSHookDirFx);
				//GC Stuff
				list_hookFSLog = null;
				return;
			}

			FileWatcher fileWatcher = new FileWatcher(this.logger);
			FileAttributes pathAttr = FileAttributes.Offline;

			try
			{
				pathAttr = File.GetAttributes(this.dict_config[name].ToString());
			}
			catch (Exception e_attr)
			{
				this.logger.LogTrace(e_attr, $"Exception getting attributes for Config::{name}. Is the path correct?");
				this.logger.LogList(list_hookFSLog, EventLogger.LogID.MSHookDirFx);
				//GC Stuff
				list_hookFSLog = null;
				fileWatcher = null;
				return;
			}

			try
			{
				if (pathAttr.HasFlag(FileAttributes.Directory))
				{
					try
					{
						fileWatcher.Callback(this._DirectoryEventCalled);
						fileWatcher.AddDirectory(name, this.dict_config[name].ToString());
						list_hookFSLog.Add($"[i] {this.dict_config[name].ToString()}");
					}
					catch (Exception e_add_dir)
					{
						this.logger.LogTrace(e_add_dir, $"[!] Cannot configure FileSystem Event polling for Config::{name}!");
						list_hookFSLog.Add($"[!] Cannot configure FileSystem Event polling for Config::{name}!");
						this.logger.LogList(list_hookFSLog, EventLogger.LogID.MSHookDirFx);
						//GC Stuff
						list_hookFSLog = null;
						fileWatcher = null;
						this.HookIntervalEvent(name, method, path);
						return;
					}
				}
				else if (File.Exists(this.dict_config[name].ToString()))
				{
					try
					{
						fileWatcher.Callback(this._FileEventCalled);
						fileWatcher.AddFile(name, this.dict_config[name].ToString());
					}
					catch (Exception e_add_file)
					{
						this.logger.LogTrace(e_add_file, $"[!] Cannot configure FileSystem Event polling for Config::{name}!");
						list_hookFSLog.Add($"[!] Cannot configure FileSystem Event polling for Config::{name}!");
						this.logger.LogList(list_hookFSLog, EventLogger.LogID.MSHookDirFx);
						//GC Stuff
						list_hookFSLog = null;
						fileWatcher = null;
						this.HookIntervalEvent(name, method, path);
						return;
					}
				}
				else
				{
					//throw new FileNotFoundException();
					//throw new DirectoryNotFoundException();
					this.logger.LogEvent($"Config::{name} - Directory does not exist or path is not a directory", EventLogger.LogID.MSHookDirFxDbg, EventLogger.ERROR);
				}
			}
			catch (Exception e_is_real)
			{
				this.logger.LogTrace(e_is_real, $"Config::{name} - Directory does not exist or path is not a directory");
				list_hookFSLog.Add($"Config::{name} - Directory does not exist or path is not a directory");
				//GC Stuff
				fileWatcher = null;
			}
			this.logger.LogList(list_hookFSLog, EventLogger.LogID.MSHookDirFx);
			//GC Stuff
			list_hookFSLog = null;
		}
		/// <summary>
		/// Add function to interval event queue.
		/// </summary>
		/// <param name="name">Name of the interval event</param>
		/// <param name="method">Function reference.</param>
		/// <param name="param">Parameter to pass to the <c>method</c></param>
		private void HookIntervalEvent(String name, Action<String> method, String param = null)
		{
			this.logger.LogEvent("MonitorService.HookIntervalEvent", EventLogger.LogID.MethodStart);
			try
			{
				this.timer_IntervalPoller.Elapsed += (sender, args) => method.Invoke(param);
			}
			catch (Exception e)
			{
				this.logger.LogTrace(e, $"[!] Cannot configure polling for {name}!");
			}
		}
		/// <summary>
		/// Logs a message that is the best available approximation of the number of bytes currently allocated in managed memory.
		/// </summary>
		private void MonitorMemory(String param = null)
		{
			this.logger.LogEvent($"GC: {GC.GetTotalMemory(false) / 1000m }kb ", EventLogger.LogID.GenericWarn, EventLogger.WARN);
		}

		/** This is not needed due to WMI having the ability to pull all disks
		private void UpdateCDrive(String param = null)
		{
			this.logger.LogEvent("MonitorService.UpdateCDrive", EventLogger.LogID.MethodStart);
			List<string> list_updateCDrive = new List<string>(5); //4

			list_updateCDrive.Add($"C Drive Free\n-------------");

			// Free space on C drive
			// bytes
			double FreeCByte = DriveInfo.GetDrives()[0].AvailableFreeSpace;
			list_updateCDrive.Add($"Bytes: {FreeCByte}");

			// kilobytes
			double FreeCKb = FreeCByte / 1024;
			list_updateCDrive.Add($"Kb: {FreeCKb}");

			// megabytes
			double FreeCMb = FreeCKb / 1024;
			list_updateCDrive.Add($"Mb: {FreeCMb}");

			// gigabytes
			double FreeCGb = FreeCMb / 1024;
			list_updateCDrive.Add($"Gb: {FreeCGb}");

			this.wmiProvider.C_FreeGB = FreeCGb;
			this.logger.LogEvent($"Gb Free on C Drive: {FreeCGb} Gb", EventLogger.LogID.MSUpdateDriveDbg, EventLogger.INFO);
			this.logger.LogList(list_updateCDrive, EventLogger.LogID.MSUpdateDrive);
			//GC Stuff
			list_updateCDrive = null;
		}
		**/
		// This break comment needs to be here so IntelliSense will pick up
		// that the definition information for the next method is not part
		// of the comment and will actually interpret it.

		/// <summary>
		/// Updates the WMI provider with information for the provided folder.
		/// <code>
		/// Also completes the following checks:
		/// - path exists and != null
		/// - number of files in the folder
		/// - alert logic based on folder path and file count
		/// </code>
		/// </summary>
		/// <param name="str_path">Full path of the folder to update information on.</param>
		private void UpdateFolder(String str_path = null)
		{
			this.logger.LogEvent("MonitorService.UpdateFolder", EventLogger.LogID.MethodStart);
			List<string> list_updateDirs = new List<string>(10); //8
			list_updateDirs.Add($"Update Folder\n----------------");
			list_updateDirs.Add($"[ i ]Path: {str_path}");

			if (str_path == null || !Directory.Exists(str_path))
			{
				list_updateDirs.Add($"[ ! ] Path is null or folder does not exist.");
				this.logger.LogList(list_updateDirs, EventLogger.LogID.MSUpdateDir);
				//GC Stuff
				list_updateDirs = null;
				return;
			}

			// C# has a very poorly put together Path/Directory manipulating system.
			// You can only do work with strings and not actually find specific parts
			// of the path as needed, such as current folder for "C:/folder1/folder2/folder3"
			// So we have to do some super obscurely named manipulation to get out "folder3"
			int fileCount = Directory.GetFiles(str_path, "*", SearchOption.TopDirectoryOnly).Length;
			String folderName = Path.GetFileName(Path.GetDirectoryName(str_path).TrimEnd(Path.DirectorySeparatorChar));
			list_updateDirs.Add($"Folder Name: {folderName}");
			if (folderName == this.dict_config["FailedDirectoryName"])
			{
				this.wmiProvider.ReportsFailed = fileCount;
				list_updateDirs.Add($"Failed Files: {fileCount} files");
				this.logger.LogEvent($"Failed Files: {fileCount} files", EventLogger.LogID.MSUpdateDirDbg, EventLogger.INFO);
				if (fileCount > 0)
				{
					this.logger.LogEvent($"Failed folder has > 0 reports!\n{this.dict_config["FailedDirectory"]}\n{fileCount} files", EventLogger.LogID.FailedGT0, EventLogger.ERROR);
					list_updateDirs.Add($"Failed folder has > 0 reports!\n{this.dict_config["FailedDirectory"]}\n{fileCount} files");
				}
			}
			if (folderName == this.dict_config["ProcessedDirectoryName"])
			{
				this.wmiProvider.ReportsProcessed = fileCount;
				list_updateDirs.Add($"Processed Files: {fileCount} files");
				this.logger.LogEvent($"Processed Files: {fileCount} files", EventLogger.LogID.MSUpdateDirDbg, EventLogger.INFO);
				if (fileCount < 1)
				{
					this.logger.LogEvent($"Processed folder has 0 reports!\n{this.dict_config["ProcessedDirectory"]}\n{fileCount} files", EventLogger.LogID.Processed0, EventLogger.ERROR);
					list_updateDirs.Add($"Processed folder has 0 reports!\n{this.dict_config["ProcessedDirectory"]}\n{fileCount} files");
				}
				else
				{
					DirectoryInfo directory = new DirectoryInfo(str_path);
					FileInfo myFile = directory.GetFiles().OrderByDescending(f => f.LastWriteTime).First();
					list_updateDirs.Add($"Most Recent: {myFile.LastWriteTime}");
					// Update WMI
					// Unable to determine how to compare epochs in SevOne, falling back to minutes elapsed
					//this.wmiProvider.ProcessedElapsed = this._getEpoch(myFile.LastWriteTime);
					this.wmiProvider.ProcessedAge = (int)DateTime.Now.Subtract(myFile.LastWriteTime).TotalMinutes;
				}
			}

			this.logger.LogList(list_updateDirs, EventLogger.LogID.MSUpdateDir);
			//GC Stuff
			list_updateDirs = null;
		}
		/// <summary>
		/// Updates the WMI provider with information for the provided file.
		/// <code>
		/// Also completes the following checks:
		/// - path exists and != null
		/// - gets last write time of file
		/// - alert logic based on file lapsed write time
		/// </code>
		/// </summary>
		/// <param name="str_path">Full path of the file to update information on.</param>
		private void UpdateFile(String str_path = null)
		{
			this.logger.LogEvent("MonitorService.UpdateFiles", EventLogger.LogID.MethodStart);

			List<string> list_updateFiles = new List<string>(10); //8
			list_updateFiles.Add($"Users file\n----------");

			if (str_path == null || !File.Exists(str_path))
			{
				list_updateFiles.Add($"Path is null or file does not exist.");
				this.logger.LogList(list_updateFiles, EventLogger.LogID.MSUpdateFile);
				//GC Stuff
				list_updateFiles = null;
				return;
			}

			// set some values
			int.TryParse((string)this.dict_config["UsersFileMaxAge"], out int int_threshold);
			double.TryParse((string)this.dict_config["PollingInterval"], out double interval);
			list_updateFiles.Add($"Threshold: {int_threshold}");

			DateTime dt_now = DateTime.Now;
			list_updateFiles.Add($"Now: {dt_now}");

			DateTime dt_lastWrite = File.GetLastWriteTime(str_path);
			list_updateFiles.Add($"Last Write: {dt_lastWrite}");

			TimeSpan ts_elapsed = dt_now.Subtract(dt_lastWrite);
			list_updateFiles.Add($"Elapsed: {ts_elapsed}");

			int int_userMinElapsed = (int)Math.Round(ts_elapsed.TotalMinutes);
			list_updateFiles.Add($"Elapsed (T_min):{int_userMinElapsed} minutes");
			// Update WMI
			// Unable to determine how to compare epochs in SevOne, falling back to minutes elapsed
			//this.wmiProvider.UserUpdatedElapsed = this._getEpoch(dt_lastWrite);
			this.wmiProvider.UserUpdatedAge = int_userMinElapsed;


			// > 2x Threshold
			if ((int_userMinElapsed > (int_threshold * 2)) & (int_userMinElapsed < ((int_threshold * 2) + interval * 2))
				| (int_userMinElapsed > (int_threshold * 2)) & this.byte_UsersFileAlerts == 0)
			{
				this.byte_UsersFileAlerts += 1;
				this.logger.LogEvent($"Users file time elapsed is greater than 2x threshold!\nThreshold: {int_threshold}\nElapsed:{int_userMinElapsed} minutes", EventLogger.LogID.Users2xMaxAge, EventLogger.ERROR);
				list_updateFiles.Add($"Users file time elapsed is greater than 2x threshold!\nThreshold: {int_threshold}\nElapsed:{int_userMinElapsed} minutes");
			}
			// Greater than threshold
			else if ((int_userMinElapsed > int_threshold) & (int_userMinElapsed < (int_threshold + interval * 2))
				| (int_userMinElapsed > int_threshold) & this.byte_UsersFileAlerts == 0)
			{
				this.byte_UsersFileAlerts += 1;
				this.logger.LogEvent($"Users file time elapsed is greater than threshold!\nThreshold: {int_threshold}\nElapsed: {int_userMinElapsed} minutes", EventLogger.LogID.UserGTMaxAge, EventLogger.WARN);
				list_updateFiles.Add($"Users file time elapsed is greater than threshold!\nThreshold: {int_threshold}\nElapsed: {int_userMinElapsed} minutes");
			}
			else if ((int_userMinElapsed < 1) | (this.byte_UsersFileAlerts == 0))
			{
				this.logger.LogEvent($"Users file updated", EventLogger.LogID.UsersUpdated, EventLogger.INFO);
			}
			this.logger.LogList(list_updateFiles, EventLogger.LogID.MSUpdateFile);
			//GC Stuff
			list_updateFiles = null;
		}
		/// <summary>
		/// Updates the WMI provider with the information for the provided service.
		/// </summary>
		/// <param name="name">Service name</param>
		private void UpdateService(String name = null)
		{
			this.logger.LogEvent("MonitorService.UpdateServices", EventLogger.LogID.MethodStart);
			List<string> list_updateServices = new List<string>(8); //6
			list_updateServices.Add($"Service\n----------");
			if (name is null)
			{
				list_updateServices.Add($"[!] Name is null!!");
				this.logger.LogList(list_updateServices, EventLogger.LogID.MSUpdateService);
				return;
			}
			list_updateServices.Add($"Name: {(string)this.dict_config[name]}");
			this.logger.LogEvent($"Name: {(string)this.dict_config[name]}", EventLogger.LogID.MSUpdateServiceDbg, EventLogger.INFO);

			string str_ServiceName = (string)this.dict_config[name];
			list_updateServices.Add($"Service Name: {str_ServiceName}");
			this.logger.LogEvent($"Service Name: {str_ServiceName}", EventLogger.LogID.MSUpdateServiceDbg, EventLogger.INFO);

			ServiceControllerStatus scs_ServiceStatus = ServiceControllerStatus.Stopped;

			try
			{
				//GC Stuff
				using (ServiceController sc = new ServiceController(str_ServiceName)) { scs_ServiceStatus = sc.Status; }
			}
			catch (Exception e)
			{
				this.logger.LogTrace(e);
				this.logger.LogList(list_updateServices, EventLogger.LogID.MSUpdateService);
				//GC Stuff
				list_updateServices = null;
				return;
			}
			list_updateServices.Add($"Service Status: {scs_ServiceStatus}");
			this.logger.LogEvent($"Service Status: {scs_ServiceStatus}", EventLogger.LogID.MSUpdateServiceDbg, EventLogger.INFO);

			if (scs_ServiceStatus != ServiceControllerStatus.Running)
			{
				this.logger.LogList(list_updateServices, EventLogger.LogID.MSUpdateService, EventLogger.WARN);
			}


			if (name == "BCAClientProcessName")
			{
				this.wmiProvider.Client_Status = scs_ServiceStatus.ToString();
			}
			else if (name == "EpicPrintServiceProcessName")
			{
				this.wmiProvider.EPS_Status = scs_ServiceStatus.ToString();
			}

			this.logger.LogList(list_updateServices, EventLogger.LogID.MSUpdateService);
			//GC Stuff
			list_updateServices = null;
		}
		/// <summary>
		/// Callback function for hooked directory events.
		/// </summary>
		/// <param name="tuple_obj">Tuple of <see cref="FileWatcher"/> instance and <see cref="FileSystemEventArgs"/></param>
		public void _DirectoryEventCalled(Tuple<FileWatcher, FileSystemEventArgs> tuple_obj)
		{
			this.logger.LogEvent("MonitorService._DirectoryEventCalled", EventLogger.LogID.MethodStart);
			FileSystemEventArgs fsEventArg = (FileSystemEventArgs)tuple_obj.Item2;
			if (fsEventArg.ChangeType == WatcherChangeTypes.Renamed) { return; }

			FileWatcher sender = (FileWatcher)tuple_obj.Item1;
			//GC Stuff
			tuple_obj = null;

			this.logger.LogEvent(sender.GetType().ToString(), EventLogger.LogID.MSCallBackDbg, EventLogger.INFO);
			this.logger.LogEvent(sender.str_ClassName, EventLogger.LogID.MSCallBackDbg, EventLogger.INFO);

			List<string> list_RptEventCBLog = new List<string>(10); //8
			list_RptEventCBLog.Add($"FileSystem Event\n------------------");
			list_RptEventCBLog.Add($"Type: {fsEventArg.ChangeType.ToString()}");
			list_RptEventCBLog.Add($"Events: {sender.short_numEvents}");
			list_RptEventCBLog.Add($"Path: {fsEventArg.FullPath}");
			list_RptEventCBLog.Add($"FileName: {fsEventArg.Name}");
			list_RptEventCBLog.Add($"Folder: {Path.GetFileName(Path.GetDirectoryName(fsEventArg.FullPath))}");
			//GC Stuff
			sender = null;

			if (fsEventArg.ChangeType == WatcherChangeTypes.Changed) { }
			if (fsEventArg.ChangeType == WatcherChangeTypes.Created) { }
			if (fsEventArg.ChangeType == WatcherChangeTypes.Deleted) { }

			this.logger.LogEvent("Memory Caching Folder Calls.", EventLogger.LogID.MSCallBackDbg, EventLogger.INFO);
			_DirCacheItemPolicy.AbsoluteExpiration = DateTimeOffset.Now.AddMilliseconds(FilesystemMonitor._CacheTimeMS);
			String name = $"UpdateFolder{Path.GetFileName(Path.GetDirectoryName(fsEventArg.FullPath))}";
			list_RptEventCBLog.Add($"MemCacheName: {name}");
			_DirMemCache.AddOrGetExisting(name, fsEventArg, _DirCacheItemPolicy);
			list_RptEventCBLog.Add($"Updated folder counts.");

			this.logger.LogList(list_RptEventCBLog, EventLogger.LogID.MSCallBackFx);
			//GC Stuff
			list_RptEventCBLog = null;
		}
		/// <summary>
		/// Callback function for hooked file events.
		/// </summary>
		/// <param name="tuple_obj">Tuple of <see cref="FileWatcher"/> instance and <see cref="FileSystemEventArgs"/></param>
		public void _FileEventCalled(Tuple<FileWatcher, FileSystemEventArgs> tuple_obj)
		{
			this.logger.LogEvent("MonitorService._FileEventCalled", EventLogger.LogID.MethodStart);
			FileSystemEventArgs fsEventArg = (FileSystemEventArgs)tuple_obj.Item2;
			if (fsEventArg.ChangeType == WatcherChangeTypes.Renamed) { tuple_obj = null; return; }

			FileWatcher sender = (FileWatcher)tuple_obj.Item1;
			//GC Stuff
			tuple_obj = null;

			this.logger.LogEvent(sender.GetType().ToString(), EventLogger.LogID.MSCallBackDbg, EventLogger.INFO);
			this.logger.LogEvent(sender.str_ClassName, EventLogger.LogID.MSCallBackDbg, EventLogger.INFO);

			List<string> list_RptEventCBLog = new List<string>(8); //6
			list_RptEventCBLog.Add($"FileSystem Event\n------------------");
			list_RptEventCBLog.Add($"Type: {fsEventArg.ChangeType.ToString()}");
			list_RptEventCBLog.Add($"Events: {sender.short_numEvents}");
			list_RptEventCBLog.Add($"Path: {fsEventArg.FullPath}");
			list_RptEventCBLog.Add($"Name: {fsEventArg.Name}");
			//GC Stuff
			sender = null;

			if (fsEventArg.ChangeType == WatcherChangeTypes.Changed) { }
			if (fsEventArg.ChangeType == WatcherChangeTypes.Created) { }
			if (fsEventArg.ChangeType == WatcherChangeTypes.Deleted) { }

			this.logger.LogEvent("Memory Caching File Calls.", EventLogger.LogID.MSCallBackDbg, EventLogger.INFO);
			_FileCacheItemPolicy.AbsoluteExpiration = DateTimeOffset.Now.AddMilliseconds(FilesystemMonitor._CacheTimeMS);
			_FileMemCache.AddOrGetExisting($"UpdateFile", fsEventArg, _FileCacheItemPolicy);
			list_RptEventCBLog.Add($"Updated file information.");

			this.logger.LogList(list_RptEventCBLog, EventLogger.LogID.MSCallBackFx);
			//GC Stuff
			list_RptEventCBLog = null;
			return;
		}
		/// <summary>
		/// Catches and processes folder update cache events 
		/// </summary>
		/// <param name="args"><see cref="CacheEntryRemovedArguments"/> with the cached <see cref="FileSystemEventArgs"/> object</param>
		private void _DirectoryUpdateCache(CacheEntryRemovedArguments args)
		{
			if (args.RemovedReason != CacheEntryRemovedReason.Expired) return;
			FileSystemEventArgs fsEventArg = (FileSystemEventArgs)args.CacheItem.Value;
			//GC Stuff
			args = null;
			this.logger.LogEvent("Memory Cache expired, update folders.", EventLogger.LogID.MSCallBackDbg, EventLogger.INFO);
			String str_fullPath = Path.GetDirectoryName(fsEventArg.FullPath) + Path.DirectorySeparatorChar;
			this.logger.LogEvent($"Event: {fsEventArg.ChangeType}\nDirectory:{str_fullPath}", EventLogger.LogID.FolderUpdated, EventLogger.INFO);
			this.logger.LogEvent($"Path: {str_fullPath}", EventLogger.LogID.MSCallBackDbg, EventLogger.INFO);
			this.UpdateFolder(str_fullPath);
		}
		/// <summary>
		/// Catches and processes file update cache events 
		/// </summary>
		/// <param name="args"><see cref="CacheEntryRemovedArguments"/> with the cached <see cref="FileSystemEventArgs"/> object</param>
		private void _FileUpdateCache(CacheEntryRemovedArguments args)
		{

			if (args.RemovedReason != CacheEntryRemovedReason.Expired) return;
			FileSystemEventArgs fsEventArg = (FileSystemEventArgs)args.CacheItem.Value;
			//GC Stuff
			args = null;
			this.logger.LogEvent("Memory Cache expired, update files.", EventLogger.LogID.MSCallBackDbg, EventLogger.INFO);
			if (!File.Exists(fsEventArg.FullPath))
			{
				this.logger.LogEvent($"File does not exist\nType: {fsEventArg.ChangeType.ToString()}\nPath: {fsEventArg.FullPath}\nName: {fsEventArg.Name}", EventLogger.LogID.UsersDeleted, EventLogger.ERROR);
				//GC Stuff
				fsEventArg = null;
				return;
			}
			if ((fsEventArg.ChangeType == WatcherChangeTypes.Deleted) && (fsEventArg.FullPath == (string)this.dict_config["UsersFilePath"]))
			{
				this.logger.LogEvent($"Users File Deleted!!\n\nType: {fsEventArg.ChangeType.ToString()}\nPath: {fsEventArg.FullPath}\nName: {fsEventArg.Name}", EventLogger.LogID.UsersDeleted, EventLogger.ERROR);
			}
			else
			{
				this.logger.LogEvent($"Event: {fsEventArg.ChangeType}\nFile:{fsEventArg.FullPath}", EventLogger.LogID.FolderUpdated, EventLogger.INFO);
			}
			this.logger.LogEvent($"Path: {fsEventArg.FullPath}", EventLogger.LogID.MSCallBackDbg, EventLogger.INFO);
			this.UpdateFile(fsEventArg.FullPath);
		}
		/// <summary>
		/// Take a <see cref="DateTime"/> object and determine the linux epoch.
		/// </summary>
		/// <param name="date">Date to convert to linux epoch.</param>
		/// <returns>Seconds elapsed since 1/1/1970 at 0000</returns>
		private uint _getEpoch(DateTime date)
		{
			DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			TimeSpan dateDiff = date.ToUniversalTime() - epoch;
			return (uint)dateDiff.TotalSeconds;
		}
		public void Stop()
		{
			this.logger.LogEvent("FileSystemMonitor.Stop", EventLogger.LogID.MethodStart);
			// Unpublish the WMI Provider
			Instrumentation.Revoke(this.wmiProvider);
			// Kill all of the FileSystemWatchers before we unload
			foreach (KeyValuePair<string, FileSystemWatcher> watcher in this.list_FileWatcher)
			{
				watcher.Value.EnableRaisingEvents = false;
				this.list_compoundLog.Add($"{watcher.Key} stopped.");
			}
		}

	}
}
