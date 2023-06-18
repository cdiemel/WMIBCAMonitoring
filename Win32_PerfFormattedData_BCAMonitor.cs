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
using System.Management;
using System.Configuration.Install;
using System.Management.Instrumentation;
using System.Diagnostics;

// Define unique namespace. 
//[assembly: Instrumented("root/cimv2/BCAMonitor")]
// [assembly: Instrumented("root/cimv2/Win32_PerfFormattedData_BCAMonitor")]
[assembly: Instrumented("root/cimv2")]

//Installs an instrumented assembly (InstallUtil.exe).
[System.ComponentModel.RunInstaller(true)]
public class InstanceInstaller : DefaultManagementProjectInstaller { }

namespace WMIFileMonitorService
{
    [InstrumentationClass(InstrumentationType.Instance)]
    public class Win32_PerfFormattedData_BCAMonitor
    {
        private int p_int_Failed;
        private int p_int_Processed;
        private int p_int_UserElapsed;
        private int p_int_ProcessedElapsed;
        private string p_eps_status;
        private string p_client_status;
        public Win32_PerfFormattedData_BCAMonitor()
        {
            p_int_Failed = 0;
            p_int_Processed = 0;
            p_int_ProcessedElapsed = 0;
            p_int_UserElapsed = 1440;
            p_eps_status = "";
            p_client_status = "";
        }
        public int ReportsFailed
        {
            get
            {
                return this.p_int_Failed;
            }
            set
            {
                this.p_int_Failed = value;
            }
        }
        public int ReportsProcessed
        {
            get
            {
                return this.p_int_Processed;
            }
            set
            {
                this.p_int_Processed = value;
            }
        }
        public int ProcessedAge
        {
            get
            {
                return this.p_int_ProcessedElapsed;
            }
            set
            {
                this.p_int_ProcessedElapsed = value;
            }
        }
        public int UserUpdatedAge
        {
            get
            {
                return this.p_int_UserElapsed;
            }
            set
            {
                this.p_int_UserElapsed = value;
            }
        }
        public string Client_Status
        {
            get
            {
                return this.p_client_status;
            }
            set
            {
                this.p_client_status = value;
            }
        }
        public string EPS_Status
        {
            get
            {
                return this.p_eps_status;
            }
            set
            {
                this.p_eps_status = value;
            }
        }
    }
}