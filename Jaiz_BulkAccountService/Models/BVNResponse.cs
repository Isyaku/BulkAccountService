using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Jaiz_BulkAccountService.Models
{
    public class BVNResponse
    {
        public string responseCode { get; set; }
        public string responseDescription { get; set; }
        public string bvn { get; set; }
        public string title { get; set; }
        public string firstName { get; set; }
        public string middleName { get; set; }
        public string lastName { get; set; }
        public string dateOfBirth { get; set; }
        public string maritalStatus { get; set; }
        public string email { get; set; }
        public string gender { get; set; }
        public string nationality { get; set; }
        public string residentialAddress { get; set; }
        public string phoneNumber1 { get; set; }
        public string phoneNumber2 { get; set; }
        public string stateOfOrigin { get; set; }
        public string stateOfResidence { get; set; }
        public string localGovernmentOfOrigin { get; set; }
        public string localGovernmentOfResidence { get; set; }
        public string registrationDate { get; set; }
        public string enrollmentBank { get; set; }
        public string enrollmentBranch { get; set; }
        public string levelOfAccount { get; set; }
        public string nationalIdentificationNumber { get; set; }
        public string nameOnCard { get; set; }
        public string watchListed { get; set; }
        public string base64Image { get; set; }
    }
}
