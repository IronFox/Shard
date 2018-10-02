using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shard;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shard.Tests
{
	[TestClass()]
	public class SerialRCSStackTests
	{

		static Random random = new Random();


		public static SerialRCSStack RandomStack(SimulationContext ctx, out RCS[] outRCSs)
		{
			int numDestinations = random.Next(3) + 1;
			int numEntries = random.Next(16);
			return RandomStack(ctx,numDestinations, numEntries, out outRCSs);
		}

		public static SerialRCSStack.DestinationTable RandomDestinations(int numDestinations)
		{
			if (numDestinations == 0)
				return new SerialRCSStack.DestinationTable();
			var field = new SerialRCSStack.Destination[numDestinations];
			int currentTimeStep = random.Next(16) + SerialRCSStack.MaxDestinationAgeTolerance + 2;
			for (int i = 0; i < numDestinations; i++)
			{
				field[i].LastUpdateTimeStep = random.Next(currentTimeStep - SerialRCSStack.MaxDestinationAgeTolerance - 2, currentTimeStep + 1);
				field[i].OldestGeneration = random.Next(currentTimeStep);
			}

			return new SerialRCSStack.DestinationTable() { All = field };
		}

		public static RCS.SerialData[] RandomEntries(SimulationContext ctx, int numEntries, out RCS[] outRCSs)
		{
			var rs = numEntries > 0 ? new RCS.SerialData[numEntries] : null;
			outRCSs = new RCS[numEntries];
			for (int j = 0; j < numEntries; j++)
			{
				outRCSs[j] = RandomRCS(ctx);
				rs[j] = outRCSs[j].Export();
			}
			return rs;
		}
		public static RCS.SerialData[] RandomEntries(SimulationContext ctx,int numEntries)
		{
			var rs = numEntries > 0 ? new RCS.SerialData[numEntries] : null;
			for (int j = 0; j < numEntries; j++)
			{
				var r = RandomRCS(ctx);
				rs[j] = r.Export();
			}
			return rs;
		}

		public static SerialRCSStack RandomStack(SimulationContext ctx, int numDestinations, int numEntries, out RCS[] outRCSs)
		{
			SerialRCSStack rs = new SerialRCSStack();
			rs.Destinations = RandomDestinations(numDestinations);
			rs.Entries = RandomEntries(ctx,numEntries, out outRCSs);
			return rs;
		}

		public static RCS RandomRCS(SimulationContext ctx)
		{
			EntityChangeSet cs = EntityChangeSetTests.RandomSet(ctx);
			return new RCS(cs, BitCubeTests.RandomIC());
		}


		[TestMethod()]
		public void IncludeNewerVersionTest()
		{
			{
				SimulationContext ctx = EntityChangeSetTests.RandomContext();

				SerialRCSStack a = new SerialRCSStack();
				a.Destinations = new SerialRCSStack.DestinationTable(2);

				SerialRCSStack b = new SerialRCSStack();
				b.Destinations = new SerialRCSStack.DestinationTable(2);

				//upper edge
				for (int i = 0; i < 100; i++)
				{
					a.Entries = RandomEntries(ctx,random.Next(16));
					b.Entries = RandomEntries(ctx, random.Next(16));

					var merged = SerialRCSStack.Merge(a, b);
					Assert.AreEqual(merged.CountEntries(), Math.Max(a.CountEntries(), b.CountEntries()));
				}

				//lower edge
				a.Entries = RandomEntries(ctx,random.Next(16));
				b.Entries = a.Entries;
				for (int i = 0; i < 100; i++)
				{
					a.Destinations.All[0] = new SerialRCSStack.Destination() { LastUpdateTimeStep = 1, OldestGeneration = random.Next(16) };
					b.Destinations.All[1] = new SerialRCSStack.Destination() { LastUpdateTimeStep = 1, OldestGeneration = random.Next(16) };

					int aRemaining = Math.Max(0,a.CountEntries() - a.Destinations.All[0].OldestGeneration);
					int bRemaining = Math.Max(0, b.CountEntries() - b.Destinations.All[1].OldestGeneration);

					int remaining = Math.Max(0, a.CountEntries() - SerialRCSStack.DestinationTable.Merge(a.Destinations, b.Destinations).GetOldestGeneration());

					var merged = SerialRCSStack.Merge(a, b);
					Assert.AreEqual(merged.CountEntries(), remaining);
					Assert.AreEqual(merged.CountEntries(), Math.Max(aRemaining, bRemaining));
				}
			}
		}

	
		[TestMethod()]
		public void GetOldestGenerationTest()
		{
			var dest = new SerialRCSStack.DestinationTable(2);

			for (int i = 0; i < 100; i++)
			{
				int gen0 = random.Next(1000);
				int gen1 = random.Next(1000);

				int lastUpdate0 = random.Next(10);
				int lastUpdate1 = random.Next(10);

				dest.All[0] = new SerialRCSStack.Destination() { LastUpdateTimeStep = lastUpdate0, OldestGeneration = gen0 };
				dest.All[1] = new SerialRCSStack.Destination() { LastUpdateTimeStep = lastUpdate1, OldestGeneration = gen1 };

				if (Math.Max(lastUpdate0, lastUpdate1) - Math.Min(lastUpdate0, lastUpdate1) <= SerialRCSStack.MaxDestinationAgeTolerance)
					Assert.AreEqual(Math.Min(gen0, gen1), dest.GetOldestGeneration());
				else
					Assert.AreEqual(lastUpdate0 < lastUpdate1 ? gen1 : gen0, dest.GetOldestGeneration());
			}
		}
	}
}