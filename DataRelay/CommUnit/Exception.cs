using System;
using System.Collections.Generic;
using System.Text;

namespace TSNY
{
    public class SetTimeoutException:Exception
    {
        public SetTimeoutException(string message)
            :base(message)
        {
        }
    }
}
