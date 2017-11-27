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

		Random random = new Random();


		SerialRCSStack RandomStack()
		{
			int numDestinations = random.Next(3) + 1;
			int numEntries = random.Next(16);
			return RandomStack(numDestinations, numEntries);
		}

		SerialRCSStack.DestinationTable RandomDestinations(int numDestinations)
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

		RCS.SerialData[] RandomEntries(int numEntries)
		{
			var rs = numEntries > 0 ? new RCS.SerialData[numEntries] : null;

			for (int j = 0; j < numEntries; j++)
			{
				rs[j] = RandomRCS().Export();
			}
			return rs;
		}

		SerialRCSStack RandomStack(int numDestinations, int numEntries)
		{
			SerialRCSStack rs = new SerialRCSStack();
			rs.Destinations = RandomDestinations(numDestinations);
			rs.Entries = RandomEntries(numEntries);
			return rs;
		}

		RCS RandomRCS()
		{
			EntityChangeSet cs = new EntityChangeSet();
			InconsistencyCoverage ic = InconsistencyCoverage.NewCommon();

			return new RCS(cs, ic);
		}


		[TestMethod()]
		public void IncludeNewerVersionTest()
		{
			{
				SerialRCSStack a = new SerialRCSStack();
				a.Destinations = new SerialRCSStack.DestinationTable(2);

				SerialRCSStack b = new SerialRCSStack();
				b.Destinations = new SerialRCSStack.DestinationTable(2);

				//upper edge
				for (int i = 0; i < 100; i++)
				{
					a.Entries = RandomEntries(random.Next(16));
					b.Entries = RandomEntries(random.Next(16));

					var merged = SerialRCSStack.Merge(a, b);
					Assert.AreEqual(merged.CountEntries(), Math.Max(a.CountEntries(), b.CountEntries()));
				}

				//lower edge
				a.Entries = RandomEntries(random.Next(16));
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

			for (int i = 0; i < 100; i++)
			{
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