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
using System.IO;
using System.Runtime.Caching;

namespace WMIFileMonitorService
{
    public class FileWatcher
	{
		/// <summary> Reference to <see cref="EventLogger"/> class for logging </summary>
		private readonly EventLogger logger;
		/// <summary> Call back for events </summary>
		private Action<Tuple<FileWatcher, FileSystemEventArgs>> func_callBack;
		/// <summary> File <see cref="MemoryCache"/> for holding generated event objects. </summary>
		private readonly MemoryCache _memCache;
		/// <summary> <see cref="CacheItemPolicy"/> for generated event objects. </summary>
		private readonly CacheItemPolicy _cacheItemPolicy;
		private FileSystemWatcher Watcher;
		/// <summary> Cache time to hold events before processing. </summary>
		private const int CacheTimeMilliseconds = 1000;
		/// <summary> Class name for sorting cache. </summary>
		public string str_ClassName;
		/// <summary> Class type for sorting cache. </summary>
		public string str_ClassType;
		/// <summary> number of events spawed, for logging. </summary>
		public short short_numEvents;

		public FileWatcher(EventLogger logClass)
		{
			this.logger = logClass;
			this.str_ClassType = this.GetType().ToString();
			this.short_numEvents = 0;
			this.logger.LogEvent($"in FileWatcher.Constructor", EventLogger.LogID.MethodStart, EventLogger.INFO);

			_memCache = MemoryCache.Default;
			_cacheItemPolicy = new CacheItemPolicy()
			{
				RemovedCallback = this._OnRemovedFromCache
			};
		}
		/// <summary>
		/// Add directory to be watched for File System events.
		/// </summary>
		/// <param name="name">Name of the directory</param>
		/// <param name="dirPath">Full path of directory</param>
		/// <param name="subDirs">Monitor subdirectories</param>
		/// <param name="filter">File filter</param>
		public void AddDirectory(string name, string dirPath, bool subDirs = false, string filter = "*.*")
		{
			FileSystemWatcher tmp_dirWatcher = new FileSystemWatcher();
			this.logger.LogEvent($"in FileWatcher.AddDirectory", EventLogger.LogID.MethodStart, EventLogger.INFO);
			this.str_ClassName = name;

			try
			{
				tmp_dirWatcher.Path = $"{Path.GetFullPath(dirPath)}\\";
			}
			catch (Exception e_add_dir)
			{
				this.logger.LogTrace(e_add_dir, $"Exception creating FileSystemWatcher for Config::{name}");
				// GC Stuff
				tmp_dirWatcher = null;
				return;
			}

			tmp_dirWatcher.NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastWrite;
			tmp_dirWatcher.Changed += _OnChanged;
			tmp_dirWatcher.Created += _OnCreated;
			tmp_dirWatcher.Deleted += _OnDeleted;
			//tmp_dirWatcher.Renamed += _OnRenamed;
			tmp_dirWatcher.Error += _OnError;

			tmp_dirWatcher.Filter = filter;
			tmp_dirWatcher.IncludeSubdirectories = subDirs;

			tmp_dirWatcher.EnableRaisingEvents = true;
			this.logger.LogEvent($"Name: {name}", EventLogger.LogID.FWAddDirDbg, EventLogger.INFO);
			this.logger.LogEvent($"dirPath: {dirPath}", EventLogger.LogID.FWAddDirDbg, EventLogger.INFO);
			this.logger.LogEvent($"subDirs: {subDirs}", EventLogger.LogID.FWAddDirDbg, EventLogger.INFO);
			this.logger.LogEvent($"Filter: {filter}", EventLogger.LogID.FWAddDirDbg, EventLogger.INFO);
			this.Watcher = tmp_dirWatcher;
		}
		/// <summary>
		/// Add file to be watched for File System events.
		/// </summary>
		/// <param name="name">Name of the file</param>
		/// <param name="dirPath">Full path of file</param>
		public void AddFile(string name, string filePath)
		{
			this.logger.LogEvent($"in FileWatcher.AddFile", EventLogger.LogID.MethodStart, EventLogger.INFO);
			List<string> list_fileLog = new List<string>(8); // 6
			list_fileLog.Add($"FileWatcher\n-------------");
			FileSystemWatcher tmp_fileWatcher = new FileSystemWatcher();
			this.str_ClassName = name;
			try
			{
				tmp_fileWatcher.Path = $"{Path.GetDirectoryName(filePath)}\\";
				this.logger.LogEvent($"dirPath: {tmp_fileWatcher.Path}", EventLogger.LogID.FWAddFileDbg, EventLogger.INFO);
				list_fileLog.Add($"dirPath: {tmp_fileWatcher.Path}");
			}
			catch (Exception e_add_dir)
			{
				this.logger.LogTrace(e_add_dir, $"Exception creating FileSystemWatcher for Config::{name}");
				this.logger.LogEvent($"Exception creating FileSystemWatcher for Config::{name}", EventLogger.LogID.FWAddFileDbg, EventLogger.INFO);
				// GC Stuff
				tmp_fileWatcher = null;
				return;
			}

			tmp_fileWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName;
			this.logger.LogEvent($"NotifyFilter: {tmp_fileWatcher.Filter}", EventLogger.LogID.FWAddFileDbg, EventLogger.INFO);
			list_fileLog.Add($"NotifyFilter: {tmp_fileWatcher.Filter}");

			tmp_fileWatcher.Changed += _OnChanged;
			tmp_fileWatcher.Created += _OnCreated;
			tmp_fileWatcher.Deleted += _OnDeleted;
			tmp_fileWatcher.Renamed += _OnRenamed;
			tmp_fileWatcher.Error += _OnError;

			tmp_fileWatcher.Filter = Path.GetFileName(filePath);
			this.logger.LogEvent($"Filter: {tmp_fileWatcher.Filter}", EventLogger.LogID.FWAddFileDbg, EventLogger.INFO);
			list_fileLog.Add($"Filter: {tmp_fileWatcher.Filter}");

			tmp_fileWatcher.IncludeSubdirectories = false;
			this.logger.LogEvent($"subDirs: {tmp_fileWatcher.IncludeSubdirectories}", EventLogger.LogID.FWAddFileDbg, EventLogger.INFO);
			list_fileLog.Add($"subDirs: {tmp_fileWatcher.IncludeSubdirectories}");

			tmp_fileWatcher.EnableRaisingEvents = true;
			this.logger.LogEvent($"Name: {name}", EventLogger.LogID.FWAddFileDbg, EventLogger.INFO);
			list_fileLog.Add($"Name: {name}");

			this.logger.LogList(list_fileLog, EventLogger.LogID.FWAddDir);
		}
		/// <summary>
		/// Add callback function for events
		/// </summary>
		/// <param name="callFunc">Function to be called after all events have been parsed</param>
		public void Callback(Action<Tuple<FileWatcher, FileSystemEventArgs>> callFunc)
		{
			this.logger.LogEvent($"in FileWatcher.Callback\n", EventLogger.LogID.MethodStart, EventLogger.INFO);

			this.func_callBack = callFunc;
		}
		/// <summary>
		/// OnChanged event handler
		/// </summary>
		private void _OnChanged(object sender, FileSystemEventArgs e)
		{
			// GC Stuff
			sender = null;
			this.logger.LogEvent($"in FileWatcher._OnChanged\n", EventLogger.LogID.MethodStart, EventLogger.INFO);
			try
			{
				_cacheItemPolicy.AbsoluteExpiration = DateTimeOffset.Now.AddMilliseconds(FileWatcher.CacheTimeMilliseconds);
				this.short_numEvents++;
				// Only add if it is not there already (swallow others)
				_memCache.AddOrGetExisting($"{this.str_ClassName}{e.ChangeType.ToString()}", Tuple.Create(this, e), _cacheItemPolicy);
			}
			catch (Exception e_cache)
			{
				this.logger.LogTrace(e_cache, "Exception adding event to cache.");
			}

		}
		/// <summary>
		/// OnCreated event handler
		/// </summary>
		private void _OnCreated(object sender, FileSystemEventArgs e)
		{
			// GC Stuff
			sender = null;
			this.logger.LogEvent($"in FileWatcher._OnCreated\n", EventLogger.LogID.MethodStart, EventLogger.INFO);
			try
			{
				_cacheItemPolicy.AbsoluteExpiration = DateTimeOffset.Now.AddMilliseconds(FileWatcher.CacheTimeMilliseconds);
				this.short_numEvents++;
				// Only add if it is not there already (swallow others)
				_memCache.AddOrGetExisting($"{this.str_ClassName}{e.ChangeType.ToString()}", Tuple.Create(this, e), _cacheItemPolicy);
			}
			catch (Exception e_cache)
			{
				this.logger.LogTrace(e_cache, "Exception adding event to cache.");
			}
		}
		/// <summary>
		/// OnDeleted event handler
		/// </summary>
		private void _OnDeleted(object sender, FileSystemEventArgs e)
		{
			// GC Stuff
			sender = null;
			this.logger.LogEvent($"in FileWatcher._OnDeleted\n", EventLogger.LogID.MethodStart, EventLogger.INFO);
			try
			{
				_cacheItemPolicy.AbsoluteExpiration = DateTimeOffset.Now.AddMilliseconds(FileWatcher.CacheTimeMilliseconds);
				this.short_numEvents++;
				// Only add if it is not there already (swallow others)
				_memCache.AddOrGetExisting($"{this.str_ClassName}{e.ChangeType.ToString()}", Tuple.Create(this, e), _cacheItemPolicy);


			}
			catch (Exception e_cache)
			{
				this.logger.LogTrace(e_cache, "Exception adding event to cache.");
			}
		}
		/// <summary>
		/// OnRenamed event handler
		/// </summary>
		private void _OnRenamed(object sender, FileSystemEventArgs e)
		{
			// GC Stuff
			sender = null;
			this.logger.LogEvent($"in FileWatcher._OnRenamed\n", EventLogger.LogID.MethodStart, EventLogger.INFO);
			try
			{
				_cacheItemPolicy.AbsoluteExpiration = DateTimeOffset.Now.AddMilliseconds(FileWatcher.CacheTimeMilliseconds);
				this.short_numEvents++;
				// Only add if it is not there already (swallow others)
				_memCache.AddOrGetExisting($"{this.str_ClassName}{e.ChangeType.ToString()}", Tuple.Create(this, e), _cacheItemPolicy);
			}
			catch (Exception e_cache)
			{
				this.logger.LogTrace(e_cache, "Exception adding event to cache.");
			}
		}
		/// <summary>
		/// OnError event handler
		/// </summary>
		private void _OnError(object sender, ErrorEventArgs e)
		{
			// GC Stuff
			sender = null;
			this.logger.LogEvent($"in FileWatcher._OnError\n", EventLogger.LogID.MethodStart, EventLogger.INFO);
			this.logger.LogTrace(e.GetException());
		}
		/// <summary>
		/// Handle events as objects are removed from the cache
		/// </summary>
		/// <param name="args"></param>
		private void _OnRemovedFromCache(CacheEntryRemovedArguments args)
		{
			this.logger.LogEvent($"in FileWatcher._OnRemovedFromCache\n", EventLogger.LogID.MethodStart, EventLogger.INFO);
			if (args.RemovedReason != CacheEntryRemovedReason.Expired) return;
			this.logger.LogEvent($"Serialized Args.CacheItem.Value:\n{args.CacheItem.Value.GetType().ToString()}", EventLogger.LogID.FWRmCacheDbg, EventLogger.INFO);

			// Now actually handle file event
			Tuple<FileWatcher, FileSystemEventArgs> tuple_Obj = (Tuple<FileWatcher, FileSystemEventArgs>)args.CacheItem.Value;
			this.func_callBack(tuple_Obj);
			this.short_numEvents = 0;
		}
	}
}
