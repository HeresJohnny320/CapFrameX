﻿using CapFrameX.Data.Session.Contracts;
using CapFrameX.Statistics.NetStandard.Contracts;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace CapFrameX.Statistics.NetStandard
{
	public static class SessionExtensions
	{

		public static IList<double> GetFrametimeTimeWindow(this ISession session, double startTime, double endTime, IFrametimeStatisticProviderOptions options, ERemoveOutlierMethod eRemoveOutlierMethod = ERemoveOutlierMethod.None)
		{
			IList<double> frametimesTimeWindow = new List<double>();
			var frametimeStatisticProvider = new FrametimeStatisticProvider(options);
			var frameStarts = session.Runs.SelectMany(r => r.CaptureData.TimeInSeconds).ToArray();
			var frametimes = frametimeStatisticProvider?.GetOutlierAdjustedSequence(session.Runs.SelectMany(r => r.CaptureData.MsBetweenPresents).ToArray(), eRemoveOutlierMethod);

			if (frametimes.Any() && frameStarts.Any())
			{
				for (int i = 0; i < frametimes.Count(); i++)
				{
					if (frameStarts[i] >= startTime && frameStarts[i] <= endTime)
					{
						frametimesTimeWindow.Add(frametimes[i]);
					}
				}
			}

			return frametimesTimeWindow;
		}

		public static IList<Point> GetFrametimePointsTimeWindow(this ISession session, double startTime, double endTime, IFrametimeStatisticProviderOptions options, ERemoveOutlierMethod eRemoveOutlierMethod = ERemoveOutlierMethod.None)
		{
			IList<Point> frametimesPointsWindow = new List<Point>();
			var frametimeStatisticProvider = new FrametimeStatisticProvider(options);

			var frametimes = frametimeStatisticProvider?.GetOutlierAdjustedSequence(session.Runs.SelectMany(r => r.CaptureData.MsBetweenPresents).ToArray(), eRemoveOutlierMethod);
			var frameStarts = session.Runs.SelectMany(r => r.CaptureData.TimeInSeconds).ToArray();
			if (frametimes.Any() && frameStarts.Any())
			{
				for (int i = 0; i < frametimes.Count(); i++)
				{
					if (frameStarts[i] >= startTime && frameStarts[i] <= endTime)
					{
						frametimesPointsWindow.Add(new Point(frameStarts[i], frametimes[i]));
					}
				}
			}

			return frametimesPointsWindow;
		}

		/// <summary>
		/// Source: https://github.com/GameTechDev/PresentMon
		/// Formular: LatencyMs =~ MsBetweenPresents + MsUntilDisplayed - previous(MsInPresentAPI)
		/// </summary>
		/// <returns></returns>
		public static IList<double> GetApproxInputLagTimes(this ISession session)
		{
			var frameTimes = session.Runs.SelectMany(r => r.CaptureData.MsBetweenPresents).ToArray();
			var appMissed = session.Runs.SelectMany(r => r.CaptureData.Dropped).ToArray();
			var untilDisplayedTimes = session.Runs.SelectMany(r => r.CaptureData.MsUntilDisplayed).ToArray();
			var inPresentAPITimes = session.Runs.SelectMany(r => r.CaptureData.MsInPresentAPI).ToArray();
			var inputLagTimes = new List<double>(frameTimes.Count() - 1);

			for (int i = 2; i < frameTimes.Count(); i++)
			{
				if (appMissed[i] != true)
					inputLagTimes.Add(frameTimes[i] + untilDisplayedTimes[i] + (0.5 * frameTimes[i - 1] ) 
						- (0.5 * inPresentAPITimes[i - 1]) - (0.5 * inPresentAPITimes[i - 2]));
			}

			return inputLagTimes;

		}
		public static IList<double> GetUpperBoundInputLagTimes(this ISession session)
		{
			var frameTimes = session.Runs.SelectMany(r => r.CaptureData.MsBetweenPresents).ToArray();
			var appMissed = session.Runs.SelectMany(r => r.CaptureData.Dropped).ToArray();
			var untilDisplayedTimes = session.Runs.SelectMany(r => r.CaptureData.MsUntilDisplayed).ToArray();
			var inPresentAPITimes = session.Runs.SelectMany(r => r.CaptureData.MsInPresentAPI).ToArray();
			var upperBoundInputLagTimes = new List<double>(frameTimes.Count() - 1);

			for (int i = 2; i < frameTimes.Count(); i++)
			{
				if (appMissed[i] != true)
					upperBoundInputLagTimes.Add(frameTimes[i] + untilDisplayedTimes[i] + frameTimes[i - 1] - inPresentAPITimes[i - 2]);
			}

			return upperBoundInputLagTimes;

		}
		public static IList<double> GetLowerBoundInputLagTimes(this ISession session)
		{
			var frameTimes = session.Runs.SelectMany(r => r.CaptureData.MsBetweenPresents).ToArray();
			var appMissed = session.Runs.SelectMany(r => r.CaptureData.Dropped).ToArray();
			var untilDisplayedTimes = session.Runs.SelectMany(r => r.CaptureData.MsUntilDisplayed).ToArray();
			var inPresentAPITimes = session.Runs.SelectMany(r => r.CaptureData.MsInPresentAPI).ToArray();
			var lowerBoundInputLagTimes = new List<double>(frameTimes.Count() - 1);

			for (int i = 2; i < frameTimes.Count(); i++)
			{
				if (appMissed[i] != true)
					lowerBoundInputLagTimes.Add(frameTimes[i] + untilDisplayedTimes[i] - inPresentAPITimes[i - 1]);
			}

			return lowerBoundInputLagTimes;

		}

		public static double GetSyncRangePercentage(this ISession session, int syncRangeLower, int syncRangeUpper)
		{
			var displayTimes = session.Runs.SelectMany(r => r.CaptureData.MsBetweenDisplayChange);
			if (!displayTimes.Any())
			{
				return 0d;
			}

			bool IsInRange(double value)
			{
				int hz = (int)Math.Round(value, 0);

				if (hz >= syncRangeLower && hz <= syncRangeUpper)
					return true;
				else
					return false;
			};

			return displayTimes.Select(time => 1000d / time)
				.Count(hz => IsInRange(hz)) / (double)displayTimes.Count();
		}

		public static IList<Point> GetGPULoadPointTimeWindow(this ISession session)
		{
			var list = new List<Point>();
			var times = session.Runs.SelectMany(r => r.SensorData.MeasureTime).ToArray();
			var loads = session.Runs.SelectMany(r => r.SensorData.GpuUsage).ToArray();

			for (int i = 0; i < times.Count(); i++)
			{
				list.Add(new Point(times[i], loads[i]));
			}
			return list;
		}

		public static IList<Point> GetCPULoadPointTimeWindow(this ISession session)
		{
			var list = new List<Point>();
			var times = session.Runs.SelectMany(r => r.SensorData.MeasureTime).ToArray();
			var loads = session.Runs.SelectMany(r => r.SensorData.CpuUsage).ToArray();

			for (int i = 0; i < times.Count(); i++)
			{
				list.Add(new Point(times[i], loads[i]));
			}
			return list;
		}

		public static IList<Point> GetCPUMaxThreadLoadPointTimeWindow(this ISession session)
		{
			var list = new List<Point>();
			var times = session.Runs.SelectMany(r => r.SensorData.MeasureTime).ToArray();
			var loads = session.Runs.SelectMany(r => r.SensorData.CpuMaxThreadUsage).ToArray();

			for (int i = 0; i < times.Count(); i++)
			{
				list.Add(new Point(times[i], loads[i]));
			}
			return list;
		}

		public static IList<Point> GetFpsPointTimeWindow(this ISession session, double startTime, double endTime, IFrametimeStatisticProviderOptions options, ERemoveOutlierMethod eRemoveOutlierMethod = ERemoveOutlierMethod.None)
		{
			return session.GetFrametimePointsTimeWindow(startTime, endTime, options, eRemoveOutlierMethod).Select(pnt => new Point(pnt.X, 1000 / pnt.Y)).ToList();
		}

		public static bool HasValidSensorData(this ISession session)
		{
			return session.Runs.All(run => run.SensorData != null && run.SensorData.MeasureTime.Any());
		}
	}
}
