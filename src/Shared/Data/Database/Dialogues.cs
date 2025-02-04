﻿using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Melia.Shared.Data.Database
{
	[Serializable]
	public class DialogData
	{
		public int ClassId { get; set; }
		public string ClassName { get; set; }
	}

	/// <summary>
	/// Dialog database.
	/// </summary>
	public class DialogDb : DatabaseJson<DialogData>
	{
		private HashSet<string> _classNames = new HashSet<string>();

		public bool Exists(string className)
		{
			return _classNames.Contains(className);
		}

		protected override void ReadEntry(JObject entry)
		{
			entry.AssertNotMissing("classId", "className");

			var info = new DialogData();

			info.ClassId = entry.ReadInt("DialogId");
			info.ClassName = entry.ReadString("className");

			this.Entries.Add(info);
		}

		protected override void AfterLoad()
		{
			foreach (var entry in this.Entries)
				_classNames.Add(entry.ClassName);
		}
	}
}
