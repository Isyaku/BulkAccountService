using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Jaiz_BulkAccountService.Models
{
    public class AccountOpeningRequest
    {
        [Required(ErrorMessage = "First Name cannot be empty")]
        public string firstname { get; set; }
        public string secondname { get; set; }
        //[Required(ErrorMessage = "Last Name cannot be empty")]
        public string lastname { get; set; }
        [Required(ErrorMessage = "Sex cannot be empty")]
        [StringLength(1, MinimumLength = 1, ErrorMessage = "Sex Field cannot be longer than 1")]
        public string sex { get; set; }
        [Required(ErrorMessage = "Address cannot be empty")]
        public string address { get; set; }
        //[Required(ErrorMessage = "Date of Birth cannot be empty")]
        public string dob { get; set; }
        [Required(ErrorMessage = "Telephone cannot be empty")]
        public string telephone { get; set; }
        [Required(ErrorMessage = "Account Name cannot be empty")]
        public string accountName { get; set; }
        //[Required(ErrorMessage = "Branch Code cannot be empty")]
        public string branchcode { get; set; }
        //[Required(ErrorMessage = "GL Code cannot be empty")]
        public string glcode { get; set; }
        public string cif { get; set; }
        [Required(ErrorMessage = "Title cannot be empty")]
        [Range(0, int.MaxValue, ErrorMessage = "Please enter valid integer number for title")]
        public int title { get; set; }
        [Required(ErrorMessage = "ID Type must be set")]
        [Range(0, int.MaxValue, ErrorMessage = "Please enter valid integer Number for ID Type")]
        public string idtype { get; set; }
        [Required(ErrorMessage = "ID number cannot be empty")]
        public string idno { get; set; }
        public string idexpirydate { get; set; }
        [Required(ErrorMessage = "Marital cannot be empty.")]
        public string marital { get; set; }
        public string addref { get; set; }
        //[Required(ErrorMessage = "Image cannot be empty")]
        public string Image { get; set; }
        //[Required(ErrorMessage = "Signature cannot be empty")]
        public string SignatureImage { get; set; }
        //[Required(ErrorMessage = "Ecosector cannot be empty")]
        [Range(0, int.MaxValue, ErrorMessage = "Please enter valid integer Number for Eco Sector")]
        public string ecosector { get; set; }
        //[Required(ErrorMessage = "division cannot be empty")]
        [Range(0, int.MaxValue, ErrorMessage = "Please enter valid integer Number for Division")]
        public int division { get; set; }
        //[Required(ErrorMessage = "Dept cannot be empty")]
        [Range(0, int.MaxValue, ErrorMessage = "Please enter valid integer Number for Dept")]
        public int dept { get; set; }
        public string bvn { get; set; }
        public string curencycode { get; set; }
        public string externalAccountNo { get; set; }
        public string externalPartyCode { get; set; }
        public string marketedbyid { get; set; }
        public string marketedforid { get; set; }
        public string channel { get; set; }
    }
}
