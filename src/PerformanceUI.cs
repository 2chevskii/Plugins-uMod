// Requires: ImageLibrary

using Newtonsoft.Json;
using Oxide.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Oxide.Plugins
{
	[Info("PerformanceUI", "2CHEVSKII", "2.0.0")]
	[Description("User friendly server performance interface")]
	class PerformanceUI : CovalencePlugin
	{

		double AvgHookTime
		{
			get
			{
				return (from plugin in Interface.Oxide.RootPluginManager.GetPlugins()
						where !plugin.IsCorePlugin && plugin.IsLoaded
						select plugin.TotalHookTime).Sum();
			}
		}

		double ServerFPS => Performance.report.frameRate;
		double ServerFrameTime => Performance.report.frameTime;


		struct PluginConfiguration
		{

		}

		class PluginSettings
		{
			[JsonProperty("Show server framerate and frametime")]
			public object ServerFPS { get; set; }
			[JsonProperty("Show server tickrate")]
			public object ServerTickrate { get; set; }
			public object EntityCount { get; set; }
			[JsonProperty("Show average hook time for last second")]
			public object AvgHookTime { get; set; }
			[JsonProperty("Show player ping value")]
			public object PlayerPing { get; set; }
		}
	}
}
