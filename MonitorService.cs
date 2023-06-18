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
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Instrumentation;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace WMIFileMonitorService
{
    public partial class MonitorService : ServiceBase
	{
		/// <summary> Full text of the MIT license. </summary>
		public static string[] LICENSE = new string[10] {
			"MIT No Attribution License\n",
			"------------------------------\n",
			"Copyright 2023 Casey Diemel\n\n",
			"Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files ",
			"(the \"Software\"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, ",
			"merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so.\n\n",
			"THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES ",
			"OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE ",
			"LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN ",
			"CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE."
		};
		EventLogger logger;
        private FilesystemMonitor fsm;
		public static String str_ServiceName = FilesystemMonitor.ServiceName;
		//public static String str_ServiceName = "BCAMonitor";
		//public static String str_ServiceName = "FileSystemMonitor";

		public MonitorService(EventLogger logger)
        {
			this.logger = logger;
            InitializeComponent();
        }

		protected override void OnStop()
		{
			this.logger.LogEvent("MonitorService.OnStop", EventLogger.LogID.MethodStart);

			// Kill all of the FileSystemWatchers before we unload
			this.fsm.Stop();

			this.logger.LogEvent($"{MonitorService.str_ServiceName} service stopped.", EventLogger.LogID.ServiceStop);
		}
		protected override void OnStart(string[] args)
		{
			this.logger.LogEvent("MonitorService.OnStart", EventLogger.LogID.MethodStart);

			this.fsm = new FilesystemMonitor(this.logger);

			this.logger.LogEvent($"{MonitorService.str_ServiceName} service started.", EventLogger.LogID.ServiceStart);
		}

	}
}
