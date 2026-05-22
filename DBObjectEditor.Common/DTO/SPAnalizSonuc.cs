using DBObjectEditor.Common.Enum;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DBObjectEditor.Common.DTO
{
    public class SPAnalizSonuc
    {
        public string MetodAd { get; set; }
        public string SPAd { get; set; }
        public List<string> InParametreler { get; set; } = new List<string>();
        public List<string> OutSutunlar { get; set; } = new List<string>();
        public ObjectTypes ObjeTuru { get; set; }
    }
}
