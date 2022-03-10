using mbia.price.CodeBehind;
using System;
using System.Data;
using System.Data.SqlClient;

namespace mbia.price.pom_upload_reminders
{
    class Program
    {
        SqlConnection conn = new SqlConnection();

        static void Main(string[] args)
        {
            try
            {
                using (var context = new Price_Entities())
                {
                    var pcrs = context.Get_POM_Pending_PCRs();                    

                    foreach (Get_POM_Pending_PCRs_Result pcr in pcrs)
                    {
                        DataTable dtExcel = new DataTable("Price_Change");
                        dtExcel.Columns.Add(new DataColumn("Action"));
                        dtExcel.Columns.Add(new DataColumn("New Group Batch"));
                        dtExcel.Columns.Add(new DataColumn("Price Change Group"));
                        dtExcel.Columns.Add(new DataColumn("Price Change Group Description"));
                        dtExcel.Columns.Add(new DataColumn("Price Change"));
                        dtExcel.Columns.Add(new DataColumn("Item"));
                        dtExcel.Columns.Add(new DataColumn("Diff"));
                        dtExcel.Columns.Add(new DataColumn("Location Type"));
                        dtExcel.Columns.Add(new DataColumn("Location"));
                        dtExcel.Columns.Add(new DataColumn("Effective Date"));
                        dtExcel.Columns.Add(new DataColumn("Updated Effective Date"));
                        dtExcel.Columns.Add(new DataColumn("Change Type"));
                        dtExcel.Columns.Add(new DataColumn("Change Value"));
                        dtExcel.Columns.Add(new DataColumn("Selling UOM"));
                        dtExcel.Columns.Add(new DataColumn("Rounding Rule"));
                        dtExcel.Columns.Add(new DataColumn("Reason"));
                        dtExcel.Columns.Add(new DataColumn("Status"));
                        dtExcel.Columns.Add(new DataColumn("Ignore Constraints"));
                        dtExcel.AcceptChanges();

                        if (pcr.Change_Type.Equals(Constants.PCR.CT_PRICE_CHANGE_ONLY) ||
                            pcr.Change_Type.Equals(Constants.PCR.CT_PRICE_CHANGE_AND_TICKET_TYPE) ||
                            pcr.Change_Type.Equals(Constants.PCR.CT_TICKET_TYPE_ONLY))
                        {
                            var pcrItems = context.Get_Items_By_PCRId(pcr.PCR_Id);
                            string oReasonCode;
                            foreach (Get_Items_By_PCRId_Result pcrItem in pcrItems)
                            {
                                switch (pcrItem.Reason_Code)
                                {
                                    case 14: oReasonCode = "Price Increase"; break;
                                    case 26: oReasonCode = "Price Decrease"; break;
                                    case 125: oReasonCode = "Price Correction"; break;
                                    case 126: oReasonCode = "MSRP"; break;
                                    case 145: oReasonCode = "In Store MD - Red Line"; break;
                                    default: oReasonCode = string.Empty; break;
                                }

                                string[] oPriceZones = pcr.Price_Zone.Split(';');
                                foreach(string oPriceZone in oPriceZones)
                                {
                                    DataRow newRow = dtExcel.NewRow();
                                    newRow["Action"] = "Create";
                                    newRow["Item"] = pcrItem.Item_Nbr;
                                    newRow["Location Type"] = (oPriceZone.Length < 3) ? "Zone" : "Store";
                                    newRow["Location"] = oPriceZone;
                                    newRow["Change Type"] = "Fixed Price";
                                    newRow["Selling UOM"] = "EA";
                                    newRow["Status"] = "Approved";
                                    newRow["Ignore Constraints"] = "No";
                                    newRow["New Group Batch"] = string.Format("{0}-{1}", pcr.PCR_Nbr, oPriceZone);
                                    newRow["Price Change Group Description"] = string.Format("{0} - {1}", oPriceZone, pcr.PCR_Desc);
                                    newRow["Effective Date"] = Methods.GetDate(pcr.Effective_Date).ToString("dd-MMM-yyyy");
                                    newRow["Change Value"] = pcrItem.New_Retail.ToString().Replace("$", "");
                                    newRow["Reason"] = pcrItem.Reason_Code;
                                    dtExcel.Rows.Add(newRow);
                                }
                            }
                        }
                        else if(pcr.Change_Type.Equals(Constants.PCR.CT_VENDED) ||
                                pcr.Change_Type.Equals(Constants.PCR.CT_VENDED_AND_TICKET_TYPE))
                        {

                        }
                    }
                }
                Console.ReadKey();
            }
            catch(Exception ex)
            {
                Methods.LogMessage(ex, "POM_Upload_Reminders.Main");
            }
        }

        void Get_POM_Pending_PCRs()
        {
            
        }

    }
}
