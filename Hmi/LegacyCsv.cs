using System.Collections.Generic;
using System.Text;

namespace Automation.Hmi;

internal static class LegacyCsv
{
	internal static List<string> Parse(string line)
	{
		List<string> list = new List<string>();
		StringBuilder stringBuilder = new StringBuilder();
		bool flag = false;
		for (int i = 0; i < (line ?? string.Empty).Length; i++)
		{
			char c = line[i];
			if (flag)
			{
				if (c == '"')
				{
					if (i + 1 < line.Length && line[i + 1] == '"')
					{
						stringBuilder.Append('"');
						i++;
					}
					else
					{
						flag = false;
					}
				}
				else
				{
					stringBuilder.Append(c);
				}
				continue;
			}
			switch (c)
			{
			case ',':
				list.Add(stringBuilder.ToString());
				stringBuilder.Clear();
				break;
			case '"':
				flag = true;
				break;
			default:
				stringBuilder.Append(c);
				break;
			}
		}
		list.Add(stringBuilder.ToString());
		return list;
	}
}


