using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MapAssist
{
public class PlayerUnitNotFoundException : Exception
    {
        private string _message;
        public PlayerUnitNotFoundException(string message)
        {
            _message = message;
        }
        public string message { get { return _message; } }
        public override string ToString()
        {
            return _message;
        }
    }
}
