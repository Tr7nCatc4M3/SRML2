using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SRML2.Config.Attributes
{
	public class ConfigCommentAttribute
	{
		public string comment;

		public ConfigCommentAttribute(string comment)
		{
			this.comment = comment;
		}
	}
}
