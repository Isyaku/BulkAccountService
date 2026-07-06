using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Jaiz_BulkAccountService.Models
{
    public class CreateAccountResponse
    {
        public string responseCode { get; set; }
        public string responseMessage { get; set; }
        public string cif { get; set; }
        public string accountNo { get; set; }
    }
}
