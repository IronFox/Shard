using Base;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
		MessagePack clientMessages;

		public List<EntityError> Errors { get { return errors; } }

		public IntermediateSDS Intermediate { get { return data; } }
		public int Generation { get { return generation; } }

		/// <summary>
		/// Time at which this computation and synchronization are complete and application should commence.
		/// Ignored during tests
		/// </summary>
		public readonly DateTime Deadline;

		private readonly EntityChange.ExecutionContext ctx;

		public SDSComputation(DateTime applicationBeginDeadline, ExtMessagePack clientMessages, TimeSpan entityLogicTimeout, EntityChange.ExecutionContext ctx)
		{
			this.ctx = ctx;
			generation = ctx.GenerationNumber;
			Deadline = applicationBeginDeadline;
			SDSStack stack = Simulation.Stack;
			SDSStack.Entry input = stack.FindGeneration(generation - 1);
			if (input == null)
				throw new IntegrityViolation("Unable to locate previous SDS at generation " + (generation-1));
			if (!input.IsFinished)
				throw new IntegrityViolation("Previous SDS at generation " + (generation - 1)+" exists but is not finished");

			//if (input.Generation != generation-1)
			//	throw new IntegrityViolation("Generation mismatch");
			old = stack.FindGeneration(generation);
			if (old == null)
				throw new IntegrityViolation("Unable to locate original SDS at generation " + generation);

			data = new IntermediateSDS();
			data.inputConsistent = input.IsFullyConsistent;

			{
				using (var ms = new MemoryStream())
				{
					input.SDS.AddToStream(ms);
					clientMessages.AddToStream(ms);
					ms.Seek(0, SeekOrigin.Begin);
					data.inputHash = new Digest(SHA256.Create().ComputeHash(ms), true);
				}
			}


			//old bad or dont exist, new maybe good
			if (clientMessages.HasBeenDiscarded)
				throw new IntegrityViolation("Available client messages are incomplete but recorded messages have been discarded");
			this.clientMessages = clientMessages.MessagePack;

			if (old.IntermediateSDS != null && old.IntermediateSDS.inputHash == data.inputHash)
			{
				data = old.IntermediateSDS;
				return;
			}



			data.entities = new EntityPool(input.SDS.FinalEntities, ctx);
			data.localChangeSet = new EntityChangeSet();
			data.ic = input.SDS.IC.Clone();
			if (!this.clientMessages.Completed)
			{
				Log.Message("Client messages are not complete for generation " + generation+", setting everything inconsistent");
				data.ic.SetAllOne();
			}
			//bool doSendClientMessages = freshClientMessages != null && freshClientMessages.ArchivedGeneration == generation;
			errors = data.localChangeSet.Evolve(input.SDS.FinalEntities, this.clientMessages.Messages, data.ic, entityLogicTimeout, ctx);
			if (errors == null && input.IsFullyConsistent && data.ic.OneCount != 0 && clientMessages.MessagePack.Completed)
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
				var oID = n.GetOutboundRCSID(Generation);
				if (generation >= n.OldestGeneration)
				{
					Log.Message("Dispatched "+oID+", IC ones="+ic.OneCount);
					n.Set(oID.ToString(), new RCS.Serial(rcs, generation));
					if (rcs.IsFullyConsistent)
						n.UploadToDB(generation, rcs);
				}
				else
					Log.Error("Recomputed generation, but remote shard will not want generated RCS");
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
					if (rcs.IC.OneCount > 0)
						Log.Message(n.Name + ": Inconsistent RCS @g"+generation+": "+rcs.IC.OneCount);
				}
				else
				{
					Log.Message(n.Name + ": Missing RCS @g"+generation);
					ic.SetOne(box);
				}
			}
			EntityPool p2 = data.entities.Clone();
			cs.Execute(p2,ic,ctx);

			SDS rs = new SDS(generation, p2.ToArray(), ic);

#if !DRY_RUN
			if (!ic.AnySet)
			{
				DB.PutAsync(new SerialSDS( rs, Simulation.ID.XYZ ), false).Wait();
			}
#endif
			Log.Message("Completed g" + Generation+" with IC ones: "+ic.OneCount+" "+Math.Round( (float)ic.OneCount / ic.Size.Product*100)+"%");
			return new Tuple<SDS, IntermediateSDS>( rs,data);
		}
	}

}
