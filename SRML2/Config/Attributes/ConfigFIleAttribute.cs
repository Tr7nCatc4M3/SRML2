using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SRML2.Config.Attributes
{
	public class ConfigFIleAttribute
	{
		internal string fileName;
		internal string defaultSection;

		public ConfigFIleAttribute(string fileName, string defaultSection)		{
			this.fileName = fileName;
			this.defaultSection = defaultSection;
		}
	}
}
