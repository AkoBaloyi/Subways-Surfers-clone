// Prototype Collection - Street Props
// Copyright (c) Amplify Creations, Lda <info@amplify.pt>

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace AmplifyCreations.PrototypeCollection.StreetProps
{
	public class Preferences
	{		
		private static readonly GUIContent StartUp = new GUIContent( "Show start screen on Unity launch" );
		public static readonly string PrefHashGUIDBase64 = "Kz/ZJqsj7kSIf9yCXV4UdA==";

		// Unity 6000 forbids Application.productName from a static field initializer, because this type
		// is touched while a ScriptableObject is being constructed. Resolving it on first read keeps the
		// same value and the same read-only usage while moving the call out of the initializer.
		private static string prefStartUp;
		public static string PrefStartUp
		{
			get
			{
				if ( prefStartUp == null )
					prefStartUp = PrefHashGUIDBase64 + Application.productName;
				return prefStartUp;
			}
		}

		public static bool GlobalStartUp = true;
	}
}