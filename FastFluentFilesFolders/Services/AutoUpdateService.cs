using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NetSparkleUpdater;
using NetSparkleUpdater.SignatureVerifiers;

namespace FastFluentFilesFolders.Services
{
	public class AutoUpdateService
	{
		public string PubKey = "EjYqgKVoBny7s0TONIzq/P5P79bn85ybLw2g3hIdxVc=";
		public string url = "https://github.com/Xhscfdj/FastFluentFilesFolders/releases/latest/downloads/appcast.xml";

		public AutoUpdateService()
		{
			var sparkle = new SparkleUpdater
			(
				url,
				new Ed25519Checker(NetSparkleUpdater.Enums.SecurityMode.Strict, PubKey)
			)
			{
				RelaunchAfterUpdate = true,
				CheckServerFileName = true,
			};
			sparkle.StartLoop(true);
		}

	}
}
