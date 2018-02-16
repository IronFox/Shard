using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace UnityShardViewer
{
	public class EntityComponent : MonoBehaviour
	{
		Vector3 from, to;
		float timeDelta,at=0;

		internal void SetState(Vector3 from, Vector3 to, float timeDelta)
		{
			this.from = from;
			this.to = to;
			this.timeDelta = timeDelta;
			this.at = 0;
		}


		public void LateUpdate()
		{
			at += Time.deltaTime;
			transform.position = Vector3.Lerp(from, to, Math.Min(at / timeDelta, 1));
		}
	}
}

