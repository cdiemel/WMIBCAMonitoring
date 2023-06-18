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
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace WMIFileMonitorService
{
    public class EventLogger
	{
		/// <summary> Current logging level. </summary>
		private short debugLVL = 2000;
		/// <summary> Windows Event Viewer log name </summary>
		private string Log;
		private string Source;
		/// <summary> Log Information level message. </summary>
		public static byte INFO = 1;
		/// <summary> Log Error level message. </summary>
		public static byte ERROR = 2;
		/// <summary> Log Warning level message. </summary>
		public static byte WARN = 3;

		/// <summary>
		/// Constructor for <see cref="EventLogger"/>
		/// </summary>
		/// <param name="log">Windows Event Viewer log name</param>
		/// <param name="dbgLvl">Logging/Debugging level</param>
		//public EventLogger(string source, string log, short dbgLvl = 2)
		public EventLogger(string log, short dbgLvl = 2)
		{
			this.setDebug(dbgLvl);
			this.Log = log;
			//this.Source = source;
		}
		/// <summary>
		/// Test Information/Warning/Error event logging.
		/// </summary>
		/// <exception cref="DivideByZeroException">Test exception</exception>
		public void testEvents()
		{
			this.WriteInfo("Test Info Message (GenericInfo)", LogID.GenericInfo);
			this.WriteWarning("Test Warn Message (Generic Warn)", LogID.GenericWarn);
			this.WriteError("Test Error Message (Generic Error)", LogID.GenericError);
			try { throw new DivideByZeroException(); }
			catch (DivideByZeroException e_dbz) { this.WriteErrorTrace(e_dbz, "Test Trace Message (Generic Error)"); }
		}
		/// <summary>
		/// Set the debug level
		/// </summary>
		/// <param name="debugLevel">
		/// <code>
		/// Level 1-4
		/// 1 - Errors and Configuration
		/// 2 - Above + Warnings
		/// 3 - Above + Information Events
		/// 4 - Above + Debug Information
		/// </code>
		/// </param>
		public void setDebug(short debugLevel = 1)
		{
			this.debugLVL = (short)((debugLevel + 1) * 1000);
			if (debugLevel == 1337)
			{
				this.debugLVL = 32000;
			}
			this.LogEvent($"Logging Level set to: {(this.debugLVL / 1000) - 1}", LogID.ELSetDebug);
		}
		/// <summary>
		/// Return the current debug level.
		/// </summary>
		/// <returns>
		/// <code>
		/// Current debug level 1-4
		/// 1 - Errors and Configuration
		/// 2 - Above + Warnings
		/// 3 - Above + Information Events
		/// 4 - Above + Debug Information
		/// </code>
		/// </returns>
		public short getDebugLvl()
		{
			if (this.debugLVL == 32000)
			{
				return 1337;
			}
			return (short)((this.debugLVL / 1000) - 1);
		}
		/// <summary>
		/// Log an event to the Windows Event Viewer
		/// </summary>
		/// <param name="message">Message to be logged.</param>
		/// <param name="eventID"><see cref="EventLogger"/>.<see cref="LogID"/> ID of the event</param>
		/// <param name="logType">
		/// <code>
		/// Type of event to log.<para />
		/// <see cref="EventLogger.INFO"/><para />
		/// <see cref="EventLogger.WARN"/><para />
		/// <see cref="EventLogger.ERROR"/><para />
		/// </code>
		/// </param>
		/// <param name="category">Windows Event Viewer Category ID</param>
		public void LogEvent(string message, EventLogger.LogID eventID, byte logType = 1, short category = 0)
		{
			//this.WriteInfo($"{id.ToString()} - {this.debugLVL.ToString()}", 51337, category);
			if ((short)eventID >= this.debugLVL) { return; }
			if (logType == EventLogger.INFO)
			{
				this.WriteInfo(message, eventID, category);
				return;
			}
			if (logType == EventLogger.ERROR)
			{
				this.WriteError(message, eventID, category);
				return;
			}
			if (logType == EventLogger.WARN)
			{
				this.WriteWarning(message, eventID, category);
				return;
			}
		}
		/// <summary>
		/// Log an error trace to the Windows Event Viewer
		/// </summary>
		/// <param name="except">Exception to be traced.</param>
		/// <param name="preMessage">Message to be logged.</param>
		/// <param name="errorID"><see cref="EventLogger"/>.<see cref="LogID"/> ID of the error</param>
		public void LogTrace(Exception except, string preMessage = null, EventLogger.LogID errorID = LogID.GenericError)
		{
			if (this.debugLVL > 4999)
			{
				this.WriteErrorTrace(except, preMessage, errorID);
				//GC Stuff
				except = null;
				return;
			}
			string msg = $"Error Message: {except.Message}";
			string type = except.GetType().Name;
			//GC Stuff
			except = null;
			if (preMessage != null)
			{
				EventLog.WriteEntry(this.Log, $"{preMessage}\n\n{msg}\nType: {type}", EventLogEntryType.Error, (int)errorID);
			}
			else
			{
				EventLog.WriteEntry(this.Log, $"{msg}\nType: {type}", EventLogEntryType.Error, (int)errorID);
			}

		}
		/// <summary>
		/// Log a list of strings, one per line, to the Windows Event Viewer.
		/// </summary>
		/// <param name="listEntries">List of lines to log.</param>
		/// <param name="eventID"><see cref="EventLogger"/>.<see cref="LogID"/> ID of the event</param>
		/// <param name="logType">
		/// <code>
		/// Type of event to log.<para />
		/// <see cref="EventLogger.INFO"/><para />
		/// <see cref="EventLogger.WARN"/><para />
		/// <see cref="EventLogger.ERROR"/><para />
		/// </code>
		/// </param>
		public void LogList(List<string> listEntries, EventLogger.LogID eventID, byte logType = 1)
		{
			if ((short)eventID >= this.debugLVL) { return; }
			this.WriteInfo(String.Join("\n", listEntries.ToArray()), eventID);
		}
		/// <summary>
		/// Write Information message to Windows Event Viewer.
		/// </summary>
		/// <param name="message">Message to be logged.</param>
		/// <param name="eventID"><see cref="EventLogger"/>.<see cref="LogID"/> ID of the event</param>
		/// <param name="category">Windows Event Viewer Category ID</param>
		private void WriteInfo(string message, EventLogger.LogID eventID, short category = 0)
		{
			EventLog.WriteEntry(this.Log, message, EventLogEntryType.Information, (int)eventID, category);
		}
		/// <summary>
		/// Write Error message to Windows Event Viewer.
		/// </summary>
		/// <param name="message">Message to be logged.</param>
		/// <param name="eventID"><see cref="EventLogger"/>.<see cref="LogID"/> ID of the event</param>
		/// <param name="category">Windows Event Viewer Category ID</param>
		private void WriteError(string message, EventLogger.LogID eventID, short category = 0)
		{
			EventLog.WriteEntry(this.Log, message, EventLogEntryType.Error, (int)eventID, category);
		}
		/// <summary>
		/// Write Error Trace message to Windows Event Viewer.
		/// </summary>
		/// <param name="except">Exception to be traced.</param>
		/// <param name="preMessage">Message to be logged.</param>
		/// <param name="errorID"><see cref="EventLogger"/>.<see cref="LogID"/> ID of the error</param>
		private void WriteErrorTrace(Exception except, string premsg = null, EventLogger.LogID errorID = LogID.GenericError)
		{
			if (except != null)
			{
				string msg = $"Error Message: {except.Message}";
				string type = except.GetType().ToString();
				string trace = except.StackTrace;
				string inner = "None";
				if (except.InnerException != null)
				{
					inner = except.InnerException.ToString();
				}
				//GC Stuff
				except = null;
				if (premsg != null)
				{
					EventLog.WriteEntry(this.Log, $"{premsg}\n\n{msg}\nType: {type}\nStacktrace:\n{trace}\n\nInner Exception\n{inner}", EventLogEntryType.Error, (int)LogID.GenericError);
				}
				else
				{
					EventLog.WriteEntry(this.Log, $"{msg}\nType: {type}\nStacktrace:\n{trace}\n\nInner Exception\n{inner}", EventLogEntryType.Error, (int)LogID.GenericError);
				}

			}
			else
			{
				StackTrace st = new StackTrace(2, true);
				EventLog.WriteEntry(this.Log, $"{premsg}\n\nUnable to print error message!\n\n\nStack Trace:\n{st.ToString()}", EventLogEntryType.Error, (int)LogID.GenericError);
			}
		}
		/// <summary>
		/// Write Warning message to Windows Event Viewer.
		/// </summary>
		/// <param name="message">Message to be logged.</param>
		/// <param name="eventID"><see cref="EventLogger"/>.<see cref="LogID"/> ID of the event</param>
		/// <param name="category">Windows Event Viewer Category ID</param>
		private void WriteWarning(string message, EventLogger.LogID eventID, short category = 0)
		{
			EventLog.WriteEntry(this.Log, message, EventLogEntryType.Warning, (int)eventID, category);
		}

		/// <summary> Enumeration of custom Log IDs for Windows Event Viewer </summary>
		public enum LogID
		{
			/** Logging Level 1 - Config and Errors **/
			/// <summary> Event ID for License messages. </summary>
			LICENSE = 0000, /* */
			ServiceStart = 1001, /* */
			ServiceStop = 1002, /* */
			ConfigLoaded = 1003, /* */
			GenericError = 1050, /* */
			MSConfigProcFail = 1311, /* */
			MSConfigFailedFail = 1312, /* */
			MSConfigUsersFail = 1313, /* */
			MSConfigClientFail = 1314, /* */
			MSConfigEPSFail = 1315, /* */
			MSConfigIntervalFail = 1316, /* */
			FailedGT0 = 1411, /* */
			Processed0 = 1412, /* */
			UsersDeleted = 1421, /* */
			Users2xMaxAge = 1422, /* */

			/** Logging Level 2 - Warnings**/
			GenericWarn = 2060, /* Generic Warning */
			FolderUpdated = 2411, /* Folder Updated */
			UserGTMaxAge = 2421, /* Users File > Config::UserFileMaxAge */
			UsersUpdated = 2422, /* Users File Updated */

			/** Logging Level 3  - In depth logging**/
			//MonitorService		= 3000, /*X in MonitorService */
			//MSOnStart			= 3100, /*X in MonitorService.OnStart */
			//MSOnStop			= 3200, /*X in MonitorService.OnStop */
			MSConfig = 3300, /* in MonitorService.LoadConfig */
			MSConfigVerify = 3310, /* in MonitorService.VerfiyConfig */
			MSAttachPoller = 3400, /* in MonitorService.AttachPollers */
			//MSHookDir			= 3500, /*X in MonitorService.hookFolders */
			//MSCallback			= 3600, /*X in MonitorService.ReportEventCalled */
			//FileWatcher			= 3700, /*X in FileWatcher */
			EventLogger = 3800, /* in EventLog */

			/** Logging Level 4 - Debug Logging**/
			GenericInfo = 4000, /* Generic Info Event */
			MSUpdateDir = 4410, /* in MonitorService.UpdateMonitors.UpdateFolders */
			MSUpdateFile = 4420, /* in MonitorService.UpdateMonitors.UpdateFiles */
			MSUpdateDrive = 4430, /* in MonitorService.UpdateMonitors.UpdateCDrive */
			MSUpdateService = 4440, /* in MonitorService.UpdateMonitors.UpdateServices */
			MSHookDirFx = 4510, /* in MonitorService.hookFolders */
			//MSHooDirCBDel		= 4520, /*X in MonitorService.UpdateMonitors */
			//MSHookDirCBAdd		= 4530, /*X in MonitorService.UpdateMonitors */
			//MSHookDirCBChange	= 4540, /*X in MonitorService.UpdateMonitors */
			MSCallBackFx = 4610, /* in FileWatcher.AddDirectory */
			FWAddDir = 4710, /* in FileWatcher.AddDirectory */
			//FWAddFile			= 4720, /*X in FileWatcher.AddFile */
			//FWRmCache			= 4730, /*X in FileWatcher._OnRemovedFromCache */
			ELSetDebug = 4810, /* in EventLog.setDebug */

			/** Logging Level 5 - Development Logging**/
			/// <summary> Event ID for the start of a method. </summary>
			MethodStart = 5000, /* */
			MSConfigDbg = 5310, /* in MonitorService.LoadConfig */
			MSConfigVerifyDbg = 5320, /* in MonitorService.VerfiyConfig */
			MSUpdateDirDbg = 5410, /* in MonitorService.UpdateMonitors.UpdateFolders */
			//MSUpdateFileDbg		= 5420, /*X in MonitorService.UpdateMonitors.UpdateFiles */
			//MSUpdateDriveDbg	= 5430, /*X in MonitorService.UpdateMonitors.UpdateCDrive */
			MSUpdateServiceDbg = 5440, /* in MonitorService.UpdateMonitors.UpdateServices */
			MSHookDirFxDbg = 5500, /* in MonitorService.hookFolders */
			MSCallBackDbg = 5600, /* in MonitorService.ReportEventCalled */
			FWAddDirDbg = 5710, /* in FileWatcher.AddDirectory */
			FWAddFileDbg = 5720, /* in FileWatcher.AddFile */
			FWRmCacheDbg = 5730, /* in FileWatcher._OnRemovedFromCache */
			//TestDebug			= 51337 /* 31137 1vl l0gg1ng*/
		}
	}
}
