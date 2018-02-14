using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VectorMath;

namespace Shard
{
	public class SDSComputation
	{
		//private SDS output;
		IntermediateSDS data;
		int generation;
		SDSStack.Entry old;
		List<EntityError> errors;
		Dictionary<Guid, EntityMessage[]> clientMessages;

		public List<EntityError> Errors { get { return errors; } }

		public IntermediateSDS Intermediate { get { return data; } }
		public int Generation { get { return generation; } }

		/// <summary>
		/// Time at which this computation will be completed.
		/// Ignored during tests
		/// </summary>
		public readonly DateTime Deadline;

		private readonly EntityChange.ExecutionContext ctx;

		public SDSComputation(DateTime stepDeadline, ClientMessageQueue freshClientMessages, TimeSpan entityLogicTimeout, EntityChange.ExecutionContext ctx)
		{
			this.ctx = ctx;
			generation = ctx.GenerationNumber;
			Deadline = stepDeadline;
			SDSStack stack = Simulation.Stack;
			SDSStack.Entry input = stack.FindGeneration(generation - 1);
			if (input == null)
				throw new IntegrityViolation("Unable to locate previous SDS at generation " + (generation-1));

			//if (input.Generation != generation-1)
			//	throw new IntegrityViolation("Generation mismatch");
			old = stack.FindGeneration(generation);
			if (old == null)
				throw new IntegrityViolation("Unable to locate original SDS at generation " + generation);

			data = new IntermediateSDS();
			data.inputConsistent = input.IsFullyConsistent;
			data.inputHash = input.SDS.HashDigest;

			clientMessages = freshClientMessages?.Collect(generation);
			if (clientMessages == null && old.SDS != null) //nothing new, check locally archived...
				clientMessages = old.SDS.ClientMessages;


			if (old.IntermediateSDS != null && old.IntermediateSDS.inputHash == data.inputHash)
			{
				data = old.IntermediateSDS;
				return;
			}



			data.entities = new EntityPool(input.SDS.FinalEntities, ctx);
			data.localChangeSet = new EntityChangeSet();
			data.ic = input.SDS.IC.Clone();
			bool doSendClientMessages = freshClientMessages != null && freshClientMessages.ArchivedGeneration == generation;
			errors = data.localChangeSet.Evolve(input.SDS.FinalEntities, clientMessages, data.ic, entityLogicTimeout,ctx);
			if (errors == null && input.IsFullyConsistent && data.ic.OneCount != 0)
				throw new IntegrityViolation("Input is fully consistent, and there are no errors. IC should have remaining empty");

			InconsistencyCoverage untrimmed = data.ic.Grow(false);
			if (untrimmed.Size != InconsistencyCoverage.CommonResolution + 2)
				throw new IntegrityViolation("IC of unsupported size: " + untrimmed.Size);
			data.ic = untrimmed.Sub(new Int3(1), new Int3(InconsistencyCoverage.CommonResolution));



			foreach (var n in Simulation.Neighbors)
			{
				IntBox remoteBox = n.ICExportRegion;
				var ic = untrimmed.Sub(remoteBox);
				RCS rcs = new RCS(new EntityChangeSet(data.localChangeSet, n.WorldSpace,ctx), ic);
				var oID = n.OutboundRCS;
				if (generation >= n.OldestGeneration)
					n.Set(oID.ToString(), new RCS.Serial( rcs,generation));
				if (rcs.IsFullyConsistent)
					n.OutStack.Put(generation, rcs);
			}
			data.localChangeSet.FilterByTargetLocation(Simulation.MySpace,ctx);
		}

		public Tuple<SDS,IntermediateSDS> Complete()
		{
			//Log.Message("Finalize SDS g" + generation); 

			var cs = data.localChangeSet.Clone();
			InconsistencyCoverage ic = data.ic.Clone();
			foreach (var n in Simulation.Neighbors)
			{
				IntBox box = n.ICImportRegion;

				var rcs = old.InboundRCS[n.LinearIndex];
				if (rcs != null)
				{
					cs.Include(rcs.CS);
					ic.Include(rcs.IC, box.Min);
				}
				else
				{
					Log.Message(n.Name + ": Missing RCS @g"+generation);
					ic.SetOne(box);
				}
			}
			EntityPool p2 = data.entities.Clone();
			cs.Execute(p2,ctx);

			SDS rs = new SDS(generation, p2.ToArray(), ic, clientMessages);

			if (!ic.AnySet)
			{
				DB.PutAsync(new SerialSDS( rs, Simulation.ID.XYZ ), false).Wait();
			}
			Log.Message("Completed g" + Generation+" with IC ones: "+ic.OneCount);
			return new Tuple<SDS, IntermediateSDS>( rs,data);
		}
	}

}
