//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated from a template.
//
//     Manual changes to this file may cause unexpected behavior in your application.
//     Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace Excavator
{
    using System;
    using System.Collections.Generic;
    
    public partial class Pledge
    {
        public Nullable<int> Individual_ID { get; set; }
        public Nullable<int> Household_ID { get; set; }
        public string Pledge_Drive_Name { get; set; }
        public string Fund_Name { get; set; }
        public string Sub_Fund_Name { get; set; }
        public Nullable<decimal> Per_Payment_Amount { get; set; }
        public string Pledge_Frequency_Name { get; set; }
        public Nullable<decimal> Total_Pledge { get; set; }
        public Nullable<System.DateTime> Start_Date { get; set; }
        public Nullable<System.DateTime> End_Date { get; set; }
    }
}
