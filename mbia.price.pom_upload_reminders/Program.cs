using mbia.price.CodeBehind;
using Microsoft.SharePoint;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Configuration;
using System.Net.Mail;
using GemBox.Spreadsheet;
using System.IO;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Globalization;

namespace mbia.price.pom_upload_reminders
{
    class Program
    {
        SqlConnection conn = new SqlConnection();        

        static void Main(string[] args)
        {
            string oReasonCode;
            DataTable dtTaxRates, dtPRICELocationTaxAreas;
            DataView oTaxRateView;
            try
            {
                using (SPSite site = new SPSite(ConfigurationManager.AppSettings["siteURL"]))
                {
                    using (SPWeb oWeb = site.OpenWeb())
                    {
                        using (var context = new Price_Entities())
                        {
                            var pcrs = context.Get_POM_Pending_PCRs();

                            #region => Create blank schemas for dtExcel & dtTicketTypeChanges <=                            

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

                            DataTable dtTicketTypeChanges = new DataTable("Item_Tickets");
                            dtTicketTypeChanges.Columns.Add(new DataColumn("Action"));
                            dtTicketTypeChanges.Columns.Add(new DataColumn("Item"));
                            dtTicketTypeChanges.Columns.Add(new DataColumn("Ticket Type Id"));
                            dtTicketTypeChanges.Columns.Add(new DataColumn("Print On Pc Ind"));
                            dtTicketTypeChanges.AcceptChanges();

                            #endregion

                            foreach (Get_POM_Pending_PCRs_Result pcr in pcrs.ToList())
                            {
                                #region => Generate Price Change Datatable to be exported to Excel <=                        

                                var pcrItems = context.Get_Items_By_PCRId(pcr.PCR_Id);

                                string idPCRNbrFormat = "0000000.##";
                                string idCounterFormat = "000.##";
                                int newGroupBatchCounter = 1;

                                string cItemNbrs = string.Empty; string cAposItemnbrs = string.Empty;
                                List<string> lstItemNbrs = new List<string>();
                                foreach (Get_Items_By_PCRId_Result pcrItem in pcrItems.ToList())
                                {
                                    if (!lstItemNbrs.Contains(pcrItem.Item_Nbr)) lstItemNbrs.Add(pcrItem.Item_Nbr);
                                }
                                cItemNbrs = String.Join(",", lstItemNbrs);
                                cAposItemnbrs = cItemNbrs.Replace(",", "','");
                                cAposItemnbrs = string.Format("{0}{1}{2}", "'", cAposItemnbrs, "'");
                                dtPRICELocationTaxAreas = Get_Location_Tax_Areas(oWeb);
                                var dTaxLocations = dtPRICELocationTaxAreas.AsEnumerable().Select(s => s.Field<string>("Title")).ToArray();
                                string cTaxLocations = string.Join(",", dTaxLocations);
                                DataTable dtRangedItemLocations = Get_Ranged_Item_Locations(cItemNbrs, cTaxLocations, oWeb);
                                DataTable dtOldTicketTypes = Get_Old_Ticket_Types(cItemNbrs, oWeb);

                                dtTaxRates = Get_Tax_Rates(oWeb);

                                pcrItems = context.Get_Items_By_PCRId(pcr.PCR_Id);

                                if (pcr.Change_Type.Equals(Constants.PCR.CT_PRICE_CHANGE_ONLY) ||
                                    pcr.Change_Type.Equals(Constants.PCR.CT_PRICE_CHANGE_AND_TICKET_TYPE) ||
                                    pcr.Change_Type.Equals(Constants.PCR.CT_TICKET_TYPE_ONLY))
                                {                            
                            
                                    foreach (Get_Items_By_PCRId_Result pcrItem in pcrItems.ToList())
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

                                        List<string> priceZonesSelected = new List<string>();
                                        if (pcr.Price_Zone.Contains("10 - Disneyland")) priceZonesSelected.Add("10");
                                        if (pcr.Price_Zone.Contains("20 - Walt Disney World")) priceZonesSelected.Add("20");
                                        foreach (string oPriceZone in priceZonesSelected)
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
                                            newRow["New Group Batch"] = string.Format("{0}{1}", Convert.ToInt32(pcr.PCR_Id).ToString(idPCRNbrFormat), (newGroupBatchCounter++).ToString(idCounterFormat));
                                            newRow["Price Change Group Description"] = string.Format("{0} - {1}", oPriceZone, pcr.PCR_Desc);
                                            newRow["Effective Date"] = Methods.GetDate(pcr.Effective_Date).ToString("dd-MMM-yy");
                                            newRow["Change Value"] = pcrItem.New_Retail.ToString().Replace("$", "");
                                            newRow["Reason"] = oReasonCode;
                                            dtExcel.Rows.Add(newRow);
                                        }
                                    }
                                }
                                else if (pcr.Change_Type.Equals(Constants.PCR.CT_VENDED) ||
                                        pcr.Change_Type.Equals(Constants.PCR.CT_VENDED_AND_TICKET_TYPE))
                                {                                                         

                                    foreach (Get_Items_By_PCRId_Result pcrItem in pcrItems.ToList())
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

                                        oTaxRateView = dtTaxRates.DefaultView;
                                        string fRetail = string.Format("{0:C2}", pcrItem.New_Retail);
                                        oTaxRateView.RowFilter = string.Format("{0} = '{1}'", Constants.Tax_Area_Retails.F_TITLE, fRetail);

                                        // Zone 10 entry
                                        DataRow newRow = dtExcel.NewRow();
                                        newRow["Action"] = "Create";
                                        newRow["Item"] = pcrItem.Item_Nbr;
                                        newRow["Location Type"] = "Zone";
                                        newRow["Location"] = "10";
                                        newRow["Change Type"] = "Fixed Price";
                                        newRow["Selling UOM"] = "EA";
                                        newRow["Status"] = "Approved";
                                        newRow["Ignore Constraints"] = "No";
                                        newRow["New Group Batch"] = string.Format("{0}{1}", Convert.ToInt32(pcr.PCR_Id).ToString(idPCRNbrFormat), (newGroupBatchCounter++).ToString(idCounterFormat));
                                        newRow["Price Change Group Description"] = string.Format("10 - {0}", pcr.PCR_Desc);
                                        newRow["Effective Date"] = Methods.GetDate(pcr.Effective_Date).ToString("dd-MMM-yy");
                                        newRow["Change Value"] = Methods.GetString(oTaxRateView[0][Constants.Tax_Area_Retails.F_ZONE10_DLR_RETAIL]).Replace("$", "");
                                        newRow["Reason"] = oReasonCode;
                                        dtExcel.Rows.Add(newRow);

                                        // Zone 20 entry
                                        newRow = dtExcel.NewRow();
                                        newRow["Action"] = "Create";
                                        newRow["Item"] = pcrItem.Item_Nbr;
                                        newRow["Location Type"] = "Zone";
                                        newRow["Location"] = "20";
                                        newRow["Change Type"] = "Fixed Price";
                                        newRow["Selling UOM"] = "EA";
                                        newRow["Status"] = "Approved";
                                        newRow["Ignore Constraints"] = "No";
                                        newRow["New Group Batch"] = string.Format("{0}{1}", Convert.ToInt32(pcr.PCR_Id).ToString(idPCRNbrFormat), (newGroupBatchCounter++).ToString(idCounterFormat));
                                        newRow["Price Change Group Description"] = string.Format("20 - {0}", pcr.PCR_Desc);
                                        newRow["Effective Date"] = Methods.GetDate(pcr.Effective_Date).ToString("dd-MMM-yy");
                                        newRow["Change Value"] = Methods.GetString(oTaxRateView[0][Constants.Tax_Area_Retails.F_ZONE20_WDW_RETAIL]).Replace("$", "");
                                        newRow["Reason"] = oReasonCode;
                                        dtExcel.Rows.Add(newRow);

                                        DataRow[] filteredRows = dtRangedItemLocations.Select(string.Format("Item = {0}", pcrItem.Item_Nbr));

                                        foreach (DataRow fRow in filteredRows)
                                        {
                                            string vLocation = Methods.GetString(fRow["LOC"]);
                                            newRow = dtExcel.NewRow();
                                            newRow["Action"] = "Create";
                                            newRow["Item"] = pcrItem.Item_Nbr;
                                            newRow["Location Type"] = "Store";
                                            newRow["Location"] = vLocation;
                                            newRow["Change Type"] = "Fixed Price";
                                            newRow["Selling UOM"] = "EA";
                                            newRow["Status"] = "Approved";
                                            newRow["Ignore Constraints"] = "No";
                                            newRow["New Group Batch"] = string.Format("{0}{1}", Convert.ToInt32(pcr.PCR_Id).ToString(idPCRNbrFormat), (newGroupBatchCounter++).ToString(idCounterFormat));
                                            newRow["Price Change Group Description"] = string.Format("{0} - {1}", pcr.PCR_Desc, vLocation);
                                            newRow["Effective Date"] = Methods.GetDate(pcr.Effective_Date).ToString("dd-MMM-yy");

                                            DataRow[] filteredLocationTaxArea = dtPRICELocationTaxAreas.Select(string.Format("Title = {0}", vLocation));
                                            try
                                            {
                                                string vTaxArea = Methods.GetString(filteredLocationTaxArea[0]["Tax_x0020_Area"]);
                                                if (vTaxArea.Contains(Constants.Location_Tax_Areas.TA_OSCEOLA))
                                                    newRow["Change Value"] = Methods.GetString(oTaxRateView[0][Constants.Tax_Area_Retails.F_OSCEOLA_RETAIL]).Replace("$", "");
                                                else if (vTaxArea.Contains(Constants.Location_Tax_Areas.TA_AULANI))
                                                    newRow["Change Value"] = Methods.GetString(oTaxRateView[0][Constants.Tax_Area_Retails.F_AULANI_RETAIL]).Replace("$", "");
                                                else if (vTaxArea.Contains(Constants.Location_Tax_Areas.TA_DCL_DLR_ODV))
                                                    newRow["Change Value"] = Methods.GetString(oTaxRateView[0][Constants.Tax_Area_Retails.F_DCL_DLR_ODV_RETAIL]).Replace("$", "");
                                            }
                                            catch (Exception ex) { }

                                            newRow["Reason"] = oReasonCode;
                                            dtExcel.Rows.Add(newRow);
                                        }
                                    }
                                }
                                #endregion                        
                        
                                #region => Generate Ticket Type Change entries <= 

                                pcrItems = context.Get_Items_By_PCRId(pcr.PCR_Id);

                                if (pcr.Change_Type.Equals(Constants.PCR.CT_PRICE_CHANGE_AND_TICKET_TYPE) ||
                                    pcr.Change_Type.Equals(Constants.PCR.CT_TICKET_TYPE_ONLY) ||
                                    pcr.Change_Type.Equals(Constants.PCR.CT_VENDED_AND_TICKET_TYPE))
                                {
                                    DataView dvOldTicketType;
                                    pcrItems = context.Get_Items_By_PCRId(pcr.PCR_Id);

                                    foreach (Get_Items_By_PCRId_Result pcrItem in pcrItems.ToList())
                                    {
                                        dvOldTicketType = dtOldTicketTypes.DefaultView;
                                        dvOldTicketType.RowFilter = string.Format("{0} = '{1}'", Constants.SF_ITEM.F_ITEM_NBR, pcrItem.Item_Nbr);

                                        string oldTicketType = Methods.GetString(dvOldTicketType[0][Constants.SF_ITEM.F_CURRENT_TICKET_TYPE]);
                                        string newTicketType = pcrItem.New_Ticket_Type;
                                        if (oldTicketType.Length > 0)
                                        {
                                            DataRow newTTRow = dtTicketTypeChanges.NewRow();
                                            newTTRow["Action"] = "Create";
                                            newTTRow["Item"] = pcrItem.Item_Nbr;
                                            newTTRow["Ticket Type Id"] = newTicketType;
                                            newTTRow["Print On Pc Ind"] = "No";
                                            dtTicketTypeChanges.Rows.Add(newTTRow);

                                            newTTRow = dtTicketTypeChanges.NewRow();
                                            newTTRow["Action"] = "Delete";
                                            newTTRow["Item"] = pcrItem.Item_Nbr;
                                            newTTRow["Ticket Type Id"] = oldTicketType;
                                            newTTRow["Print On Pc Ind"] = "No";
                                            dtTicketTypeChanges.Rows.Add(newTTRow);
                                        }
                                    }
                                }

                                #endregion                        
                            }

                            #region => Send email to POM users <=

                            string recipients, subject, body;
                            recipients = Get_Receipients_By_Dept_By_Role("NA", Constants.UserRoles.ROLE_POM, oWeb);
                            //recipients = "rahul.vartak@disney.com"; 
                            SPListItem emailTemplate = DataLayer.Get_From_Email_Template(oWeb, Constants.EmailTemplate.KEY_POM_UPLOAD_REMINDER);

                            if (null != emailTemplate)
                            {
                                subject = Methods.GetString(emailTemplate[Constants.EmailTemplate.FLD_SUBJECT]);
                                pcrs = context.Get_POM_Pending_PCRs();
                                if (pcrs.Count() > 0)
                                {
                                    body = Methods.GetString(emailTemplate[Constants.EmailTemplate.FLD_BODY]);

                                    if (((null == dtExcel) || (dtExcel.Rows.Count < 150)) && ((null == dtTicketTypeChanges) || (dtTicketTypeChanges.Rows.Count < 150)))
                                        SendEmail_With_Ods_Attachment_Through_Batch(recipients, subject, body, string.Empty, dtExcel, dtTicketTypeChanges, string.Format("PCR_Upload_{0}", DateTime.Today.ToString("MM_dd_yyyy", CultureInfo.InvariantCulture)));
                                    else
                                        SendEmail_With_Xlsx_Attachment_Through_Batch(recipients, subject, body, string.Empty, dtExcel, dtTicketTypeChanges, string.Format("PCR_Upload_{0}", DateTime.Today.ToString("MM_dd_yyyy", CultureInfo.InvariantCulture)));
                                }
                                else
                                {
                                    body = "No Price Change Requests were found to be processed by POM";

                                    MailMessage mail = new MailMessage();
                                    SmtpClient client = new SmtpClient();
                                    client.Port = 25;
                                    client.Timeout = 10000;
                                    mail.From = new MailAddress("no-reply.price.sharepoint@disney.com");
                                    string[] toAddresses = recipients.Split(';');
                                    foreach (string address in toAddresses)
                                    {
                                        if (address.Trim().Length == 0 || mail.To.Contains(new MailAddress(address))) continue;
                                        else mail.To.Add(new MailAddress(address));
                                    }

                                    mail.Subject = subject;
                                    mail.Body = body;
                                    mail.IsBodyHtml = true;
                                    mail.Priority = MailPriority.Low;
                                    client.DeliveryMethod = SmtpDeliveryMethod.Network;
                                    client.Host = Constants.Common.SMTP_HOST;
                                    client.Credentials = new System.Net.NetworkCredential(Constants.Common.SMTP_USER, Constants.Common.SMTP_PWD, Constants.Common.SMTP_USER_DOMAIN);
                                    client.Send(mail);
                                }
                            }

                            #endregion
                        }
                    }
                }

                LogMessage("Batch process for sending POM Upload reminders completed...", "pom_upload_reminders.Main");
                //Console.ReadKey();
            }
            catch (Exception ex)
            {
                Methods.LogMessage(ex, "POM_Upload_Reminders.Main");
            }
        }

        private static DataTable Get_Old_Ticket_Types(string cItemNbrs, SPWeb oWeb)
        {
            DataTable dtOldTicketTypes = new DataTable();
            SqlConnection conn = new SqlConnection();
            try
            {
                conn.ConnectionString = Get_Connection_String(oWeb);
                SqlCommand cmd = new SqlCommand("Get_Current_Ticket_Types_Of_Items", conn);
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandTimeout = Convert.ToInt32(Get_Configuration(Constants.Common.KEY_SQL_TIMEOUT, oWeb));
                cmd.Parameters.Add(new SqlParameter("itemNbrs", SqlDbType.VarChar)).Value = cItemNbrs;                
                SqlDataAdapter oAdapter = new SqlDataAdapter(cmd);
                oAdapter.Fill(dtOldTicketTypes);
            }
            catch(Exception ex)
            {
                Methods.LogMessage(ex, "POM_Upload_Reminders.Get_Old_Ticket_Types");
            }
            return dtOldTicketTypes;
        }

        private static string Get_Connection_String(SPWeb oWeb)
        {
            string conn = string.Empty;
            try
            {
                return string.Format("{0}; Connection Timeout={1}", Get_Configuration(Constants.Common.SQL_CONN, oWeb), Get_Configuration(Constants.Common.KEY_SQL_TIMEOUT, oWeb));
            }
            catch (Exception ex)
            {
                Methods.LogMessage(ex, "Get_Connection_String");
            }
            return conn;
        }

        public static string Get_Receipients_By_Dept_By_Role(string dept, string role, SPWeb oWeb)
        {
            string recipients = string.Empty;
            try
            {
                string debugMode = Get_Configuration(Constants.Configuration.KEY_DEBUG_MODE, oWeb);
                if (debugMode.Equals("No", StringComparison.InvariantCulture) || debugMode.Equals("false", StringComparison.InvariantCulture) || debugMode.Equals("0"))
                {
                    // do nothing
                }
                else
                {
                    switch (role)
                    {
                        case Constants.UserRoles.ROLE_MPI:
                            return Get_Configuration(Constants.Configuration.KEY_DEBUG_MPI_USERS, oWeb);
                        case Constants.UserRoles.ROLE_PRICER:
                            return Get_Configuration(Constants.Configuration.KEY_DEBUG_PRICING_USERS, oWeb);
                        case Constants.UserRoles.ROLE_PLANNING:
                            return Get_Configuration(Constants.Configuration.KEY_DEBUG_PLANNING_USERS, oWeb);
                        case Constants.UserRoles.ROLE_MP:
                            return Get_Configuration(Constants.Configuration.KEY_DEBUG_MP_USERS, oWeb);
                        case Constants.UserRoles.ROLE_POM:
                            return Get_Configuration(Constants.Configuration.KEY_DEBUG_POM_USERS, oWeb);
                        default:
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Methods.LogMessage(ex, "POM_Upload_Reminders.Get_Receipients_By_Dept_By_Role");
            }
            return recipients;
        }

        public static string Get_Configuration(string keyword, SPWeb oWeb)
        {
            string conn = string.Empty;            
            SPList oList = oWeb.Lists.TryGetList(Constants.Configuration.NAME);
            if (null != oList)
            {
                SPQuery oQuery = new SPQuery();
                oQuery.Query = string.Format(Constants.Configuration.GET_CONFIG_BY_TEXT, keyword);
                oQuery.ViewFields = Constants.Configuration.VF_VALUE;
                SPListItemCollection items = oList.GetItems(oQuery);
                if (null != items && items.Count > 0)
                {
                    return Methods.RemoveAllHtmlTag(Convert.ToString(items[0][Constants.Configuration.FLD_VALUE]));
                }
            }
            return conn;
        }

        public static DataTable Get_Location_Tax_Areas(SPWeb oWeb)
        {
            try
            {
                SPList oList = oWeb.Lists.TryGetList(Constants.Location_Tax_Areas.NAME);
                if (null != oList)
                {
                    SPQuery oQuery = new SPQuery();
                    oQuery.Query = Constants.Location_Tax_Areas.Q_GET_ALL;
                    oQuery.ViewFields = Constants.Location_Tax_Areas.VF_ALL;
                    SPListItemCollection items = oList.GetItems(oQuery);
                    if (null != items && items.Count > 0)
                        return items.GetDataTable();
                }
            }
            catch (Exception ex)
            {
                Methods.LogMessage(ex, "POM_Upload_Reminders.Get_Location_Tax_Areas");
            }
            return new DataTable();
        }

        public static DataTable Get_Ranged_Item_Locations(string cItemNbrs, string cTaxLocations, SPWeb oWeb)
        {
            SqlConnection conn = new SqlConnection();
            DataTable dtRangedItemLocations = new DataTable();
            try
            {
                conn.ConnectionString = Get_Connection_String(oWeb);
                SqlCommand cmd = new SqlCommand("Confirm_Ranged_Locations_For_Items", conn);
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandTimeout = Convert.ToInt32(Get_Configuration(Constants.Common.KEY_SQL_TIMEOUT, oWeb));
                cmd.Parameters.Add(new SqlParameter("itemNbrs", SqlDbType.VarChar)).Value = cItemNbrs;
                cmd.Parameters.Add(new SqlParameter("locations", SqlDbType.VarChar)).Value = cTaxLocations;
                SqlDataAdapter oAdapter = new SqlDataAdapter(cmd);
                oAdapter.Fill(dtRangedItemLocations);
            }
            catch (Exception ex)
            {
                Methods.LogMessage(ex, "POM_Upload_Reminders.Get_Ranged_Item_Locations");
            }
            finally
            {
                if (null != conn)
                {
                    conn.Close();
                }
            }
            return dtRangedItemLocations;
        }

        public static DataTable Get_Tax_Rates(SPWeb oWeb)
        {
            try
            {
                SPList oList = oWeb.Lists.TryGetList(Constants.Tax_Area_Retails.NAME);
                if (null != oList)
                {
                    SPQuery oQuery = new SPQuery();
                    oQuery.Query = Constants.Tax_Area_Retails.Q_GET_ALL_RETAILS;
                    oQuery.ViewFields = Constants.Tax_Area_Retails.VF_ALL_RETAILS;
                    SPListItemCollection items = oList.GetItems(oQuery);
                    if (null != items && items.Count > 0)
                        return items.GetDataTable();
                }
            }
            catch (Exception ex)
            {
                Methods.LogMessage(ex, "POM_Upload_Reminders.Get_Tax_Rates");
            }
            return new DataTable();
        }

        public static void SendEmail_With_Ods_Attachment_Through_Batch(string recipients, string subject, string body, string cc, DataTable dtContents, DataTable dtTicketTypeChanges, string attachmentName)
        {
            try
            {
                SpreadsheetInfo.SetLicense("FREE-LIMITED-KEY");

                using (var memoryStream = new MemoryStream())
                {
                    var workbook = new ExcelFile();
                    var worksheet = workbook.Worksheets.Add("Price_Change");
                    worksheet.InsertDataTable(dtContents, new InsertDataTableOptions()
                    {
                        ColumnHeaders = true,
                        StartRow = 0
                    });

                    worksheet.Rows[0].Style.Font.Weight = ExcelFont.BoldWeight;
                    worksheet.Columns[1].Width = 22 * 256;
                    worksheet.Columns[2].Width = 20 * 256;
                    worksheet.Columns[3].Width = 50 * 256;
                    worksheet.Columns[4].Width = 12 * 256;
                    worksheet.Columns[5].Width = 15 * 256;
                    worksheet.Columns[6].Width = 5 * 256;
                    worksheet.Columns[7].Width = 15 * 256;
                    worksheet.Columns[8].Width = 12 * 256;
                    worksheet.Columns[9].Width = 13 * 256;
                    worksheet.Columns[10].Width = 20 * 256;
                    worksheet.Columns[11].Width = 12 * 256;
                    worksheet.Columns[12].Width = 12 * 256;
                    worksheet.Columns[13].Width = 13 * 256;
                    worksheet.Columns[14].Width = 11 * 256;
                    worksheet.Columns[15].Width = 15 * 256;
                    worksheet.Columns[16].Width = 13 * 256;
                    worksheet.Columns[17].Width = 9 * 256;
                    worksheet.Columns[18].Width = 16 * 256;

                    workbook.Save(memoryStream, SaveOptions.OdsDefault);

                    memoryStream.Seek(0, SeekOrigin.Begin);
                    var attachment = new Attachment(memoryStream, string.Format("{0}.ods", attachmentName));
                    attachment.ContentDisposition.CreationDate = DateTime.Now;
                    attachment.ContentDisposition.ModificationDate = DateTime.Now;
                    attachment.ContentDisposition.Inline = false;
                    attachment.ContentDisposition.Size = memoryStream.Length;
                    attachment.ContentType.MediaType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

                    using (var secondMemoryStream = new MemoryStream())
                    {
                        MailMessage mail = new MailMessage();
                        SmtpClient client = new SmtpClient();
                        client.Port = 25;
                        client.Timeout = 10000;
                        mail.From = new MailAddress("no-reply.price.sharepoint@disney.com");
                        string[] toAddresses = recipients.Split(';');
                        foreach (string address in toAddresses)
                        {
                            if (address.Trim().Length == 0 || mail.To.Contains(new MailAddress(address))) continue;
                            else mail.To.Add(new MailAddress(address));
                        }

                        mail.Subject = subject;
                        mail.Body = body;
                        mail.IsBodyHtml = true;
                        mail.Attachments.Add(attachment);

                        if (dtTicketTypeChanges.Rows.Count > 0)
                        {
                            var secondWorkbook = new ExcelFile();
                            var secondWorksheet = secondWorkbook.Worksheets.Add("Item_Tickets");
                            secondWorksheet.InsertDataTable(dtTicketTypeChanges, new InsertDataTableOptions() { ColumnHeaders = true, StartRow = 0 });
                            secondWorksheet.Rows[0].Style.Font.Weight = ExcelFont.BoldWeight;
                            secondWorksheet.Columns[1].Width = 15 * 256;
                            secondWorksheet.Columns[2].Width = 16 * 256;
                            secondWorksheet.Columns[3].Width = 16 * 256;
                            secondWorkbook.Save(secondMemoryStream, SaveOptions.OdsDefault);

                            secondMemoryStream.Seek(0, SeekOrigin.Begin);

                            var secondAttachment = new Attachment(secondMemoryStream, string.Format("{0}.ods", attachmentName.Replace("PCR", "TicketType")));
                            secondAttachment.ContentDisposition.CreationDate = DateTime.Now;
                            secondAttachment.ContentDisposition.ModificationDate = DateTime.Now;
                            secondAttachment.ContentDisposition.Inline = false;
                            secondAttachment.ContentDisposition.Size = secondMemoryStream.Length;
                            secondAttachment.ContentType.MediaType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

                            mail.Attachments.Add(secondAttachment);
                        }


                        mail.Priority = MailPriority.High;
                        client.DeliveryMethod = SmtpDeliveryMethod.Network;
                        client.Host = Constants.Common.SMTP_HOST;
                        client.Credentials = new System.Net.NetworkCredential(Constants.Common.SMTP_USER, Constants.Common.SMTP_PWD, Constants.Common.SMTP_USER_DOMAIN);
                        client.Send(mail);
                    }
                }
            }
            catch (Exception ex)
            {
                Methods.LogMessage(ex, "Methods.SendEmail_With_Ods_Attachment");
            }
        }

        internal static void SendEmail_With_Xlsx_Attachment_Through_Batch(string recipients, string subject, string body, string cc, DataTable dtContents, DataTable dtTicketTypeChanges, string attachmentName)
        {
            try
            {
                using (var memoryStream = new MemoryStream())
                {
                    using (var workbook = SpreadsheetDocument.Create(memoryStream, SpreadsheetDocumentType.Workbook))
                    {
                        var workbookPart = workbook.AddWorkbookPart();
                        workbook.WorkbookPart.Workbook = new Workbook();
                        workbook.WorkbookPart.Workbook.Sheets = new Sheets();
                        AddStyleSheet(workbook);

                        #region => Add 1st Worksheet <=

                        var sheetPart = workbook.WorkbookPart.AddNewPart<WorksheetPart>();
                        sheetPart.Worksheet = new Worksheet();

                        Columns oColumns = new Columns();
                        oColumns.Append(new Column() { Min = 1, Max = 1, Width = 8, CustomWidth = true });
                        oColumns.Append(new Column() { Min = 2, Max = 2, Width = 18, CustomWidth = true });
                        oColumns.Append(new Column() { Min = 3, Max = 3, Width = 20, CustomWidth = true });
                        oColumns.Append(new Column() { Min = 4, Max = 4, Width = 30, CustomWidth = true });
                        oColumns.Append(new Column() { Min = 5, Max = 5, Width = 12, CustomWidth = true });
                        oColumns.Append(new Column() { Min = 6, Max = 6, Width = 13, CustomWidth = true });
                        oColumns.Append(new Column() { Min = 7, Max = 7, Width = 5, CustomWidth = true });
                        oColumns.Append(new Column() { Min = 8, Max = 8, Width = 12, CustomWidth = true });
                        oColumns.Append(new Column() { Min = 9, Max = 9, Width = 10, CustomWidth = true });
                        oColumns.Append(new Column() { Min = 10, Max = 10, Width = 12, CustomWidth = true });
                        oColumns.Append(new Column() { Min = 11, Max = 11, Width = 20, CustomWidth = true });
                        oColumns.Append(new Column() { Min = 12, Max = 12, Width = 11, CustomWidth = true });
                        oColumns.Append(new Column() { Min = 13, Max = 14, Width = 12, CustomWidth = true });
                        oColumns.Append(new Column() { Min = 15, Max = 15, Width = 13, CustomWidth = true });
                        oColumns.Append(new Column() { Min = 16, Max = 16, Width = 25, CustomWidth = true });
                        oColumns.Append(new Column() { Min = 17, Max = 17, Width = 10, CustomWidth = true });
                        oColumns.Append(new Column() { Min = 18, Max = 18, Width = 17, CustomWidth = true });

                        sheetPart.Worksheet.Append(oColumns);
                        var sheetData = sheetPart.Worksheet.AppendChild(new SheetData());

                        Sheets sheets = workbook.WorkbookPart.Workbook.GetFirstChild<Sheets>();
                        string relationshipId = workbook.WorkbookPart.GetIdOfPart(sheetPart);

                        uint sheetId = 1;
                        if (sheets.Elements<Sheet>().Count() > 0)
                        {
                            sheetId = sheets.Elements<Sheet>().Select(s => s.SheetId.Value).Max() + 1;
                        }

                        Sheet sheet = new Sheet() { Id = relationshipId, SheetId = sheetId, Name = dtContents.TableName };
                        sheets.Append(sheet);

                        Row headerRow = new Row();

                        List<String> columns = new List<string>();
                        foreach (DataColumn column in dtContents.Columns)
                        {
                            columns.Add(column.ColumnName);

                            Cell cell = new Cell() { StyleIndex = Convert.ToUInt32(1) };
                            cell.DataType = CellValues.String;
                            cell.CellValue = new CellValue(column.ColumnName);
                            headerRow.AppendChild(cell);
                        }
                        sheetData.AppendChild(headerRow);

                        foreach (DataRow dsrow in dtContents.Rows)
                        {
                            Row newRow = new Row();
                            foreach (String col in columns)
                            {
                                Cell cell = new Cell();
                                cell.DataType = CellValues.String;
                                cell.CellValue = new CellValue(dsrow[col].ToString());
                                newRow.AppendChild(cell);
                            }
                            sheetData.AppendChild(newRow);
                        }

                        #endregion

                        //#region => Add 2nd Worksheet <=

                        //var sheetPart_2nd = workbook.WorkbookPart.AddNewPart<WorksheetPart>();
                        //sheetPart_2nd.Worksheet = new Worksheet();
                        //var sheetData_2nd = sheetPart_2nd.Worksheet.AppendChild(new SheetData());
                        //Sheets sheets_2nd = workbook.WorkbookPart.Workbook.Sheets;
                        //string relationshipId_2nd = workbook.WorkbookPart.GetIdOfPart(sheetPart_2nd);

                        //sheetId = 1;
                        
                        //if (sheets_2nd.Elements<Sheet>().Count() > 0)
                        //{
                        //    sheetId = sheets_2nd.Elements<Sheet>().Select(s => s.SheetId.Value).Max() + 1;
                        //}

                        //Sheet sheet_Second = new Sheet() { Id = relationshipId_2nd, SheetId = sheetId, Name = dtTicketTypeChanges.TableName };
                        //sheets_2nd.Append(sheet_Second);

                        //headerRow = new Row();

                        //columns = new List<string>();
                        //foreach (DataColumn column in dtTicketTypeChanges.Columns)
                        //{
                        //    columns.Add(column.ColumnName);

                        //    Cell cell = new Cell() { StyleIndex = Convert.ToUInt32(1) };
                        //    cell.DataType = CellValues.String;
                        //    cell.CellValue = new CellValue(column.ColumnName);
                        //    headerRow.AppendChild(cell);
                        //}
                        //sheetData_2nd.AppendChild(headerRow);

                        //foreach (DataRow dsrow in dtContents.Rows)
                        //{
                        //    Row newRow = new Row();
                        //    foreach (String col in columns)
                        //    {
                        //        Cell cell = new Cell();
                        //        cell.DataType = CellValues.String;
                        //        cell.CellValue = new CellValue(dsrow[col].ToString());
                        //        newRow.AppendChild(cell);
                        //    }
                        //    sheetData_2nd.AppendChild(newRow);
                        //}

                        //#endregion
                    }

                    memoryStream.Seek(0, SeekOrigin.Begin);
                    var attachment = new Attachment(memoryStream, string.Format("{0}.xlsx", attachmentName));
                    attachment.ContentDisposition.CreationDate = DateTime.Now;
                    attachment.ContentDisposition.ModificationDate = DateTime.Now;
                    attachment.ContentDisposition.Inline = false;
                    attachment.ContentDisposition.Size = memoryStream.Length;
                    //attachment.ContentType.MediaType = "application/vnd.oasis.opendocument.spreadsheet";
                    attachment.ContentType.MediaType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

                    using (var secondMemoryStream = new MemoryStream())
                    {
                        MailMessage mail = new MailMessage();
                        SmtpClient client = new SmtpClient();
                        client.Port = 25;
                        client.Timeout = 10000;
                        mail.From = new MailAddress("no-reply.price.sharepoint@disney.com");

                        string[] toAddresses = recipients.Split(';');
                        foreach (string address in toAddresses)
                        {
                            if (address.Trim().Length == 0 || mail.To.Contains(new MailAddress(address))) continue;
                            else mail.To.Add(new MailAddress(address));
                        }

                        mail.Subject = subject;
                        mail.Body = body;
                        mail.IsBodyHtml = true;
                        mail.Attachments.Add(attachment);

                        if (dtTicketTypeChanges.Rows.Count > 0)
                        {
                            using (var workbook2nd = SpreadsheetDocument.Create(secondMemoryStream, SpreadsheetDocumentType.Workbook))
                            {
                                var workbookPart2nd = workbook2nd.AddWorkbookPart();
                                workbook2nd.WorkbookPart.Workbook = new Workbook();
                                workbook2nd.WorkbookPart.Workbook.Sheets = new Sheets();
                                AddStyleSheet(workbook2nd);

                                var sheetPart2nd = workbook2nd.WorkbookPart.AddNewPart<WorksheetPart>();
                                sheetPart2nd.Worksheet = new Worksheet();


                                var sheetData2nd = sheetPart2nd.Worksheet.AppendChild(new SheetData());

                                Sheets sheets2nd = workbook2nd.WorkbookPart.Workbook.GetFirstChild<Sheets>();
                                string relationshipId = workbook2nd.WorkbookPart.GetIdOfPart(sheetPart2nd);

                                uint sheetId = 1;
                                if (sheets2nd.Elements<Sheet>().Count() > 0)
                                {
                                    sheetId = sheets2nd.Elements<Sheet>().Select(s => s.SheetId.Value).Max() + 1;
                                }

                                Sheet sheet2nd = new Sheet() { Id = relationshipId, SheetId = sheetId, Name = dtTicketTypeChanges.TableName };
                                sheets2nd.Append(sheet2nd);

                                Row headerRow = new Row();

                                List<String> columns = new List<string>();
                                foreach (DataColumn column in dtTicketTypeChanges.Columns)
                                {
                                    columns.Add(column.ColumnName);

                                    Cell cell = new Cell() { StyleIndex = Convert.ToUInt32(1) };
                                    cell.DataType = CellValues.String;
                                    cell.CellValue = new CellValue(column.ColumnName);
                                    headerRow.AppendChild(cell);
                                }
                                sheetData2nd.AppendChild(headerRow);

                                foreach (DataRow dsrow in dtTicketTypeChanges.Rows)
                                {
                                    Row newRow = new Row();
                                    foreach (String col in columns)
                                    {
                                        Cell cell = new Cell();
                                        cell.DataType = CellValues.String;
                                        cell.CellValue = new CellValue(dsrow[col].ToString());
                                        newRow.AppendChild(cell);
                                    }
                                    sheetData2nd.AppendChild(newRow);
                                }
                            }

                            secondMemoryStream.Seek(0, SeekOrigin.Begin);
                            var attachment2nd = new Attachment(secondMemoryStream, string.Format("{0}.xlsx", attachmentName.Replace("PCR", "TicketType")));
                            attachment2nd.ContentDisposition.CreationDate = DateTime.Now;
                            attachment2nd.ContentDisposition.ModificationDate = DateTime.Now;
                            attachment2nd.ContentDisposition.Inline = false;
                            attachment2nd.ContentDisposition.Size = secondMemoryStream.Length;
                            //attachment.ContentType.MediaType = "application/vnd.oasis.opendocument.spreadsheet";
                            attachment2nd.ContentType.MediaType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                            mail.Attachments.Add(attachment2nd);
                        }


                        mail.Priority = MailPriority.High;
                        client.DeliveryMethod = SmtpDeliveryMethod.Network;
                        client.Host = "wmfloapv0001.wdw.disney.com";
                        client.Credentials = new System.Net.NetworkCredential("4bosimba", "Simba911", "wdw");
                        client.Send(mail);
                    }
                }
            }
            catch (Exception ex)
            {
                Methods.LogMessage(ex, "Methods.SendEmailWithAttachment");
            }
        }

        internal static void AddStyleSheet(SpreadsheetDocument spreadsheet)
        {
            WorkbookStylesPart stylesheet = spreadsheet.WorkbookPart.AddNewPart<WorkbookStylesPart>();

            Stylesheet workbookstylesheet = new Stylesheet();

            Font font0 = new Font();         // Default font

            Font font1 = new Font();         // Bold font
            Bold bold = new Bold();
            font1.Append(bold);

            Fonts fonts = new Fonts();      // <APENDING Fonts>
            fonts.Append(font0);
            fonts.Append(font1);

            // <Fills>
            Fill fill0 = new Fill();        // Default fill
            PatternFill oPatternFill = new PatternFill() { PatternType = PatternValues.Solid };
            ForegroundColor oFColor = new ForegroundColor() { Rgb = "FFDCE2E6" /*"FFC00000"*/ };
            BackgroundColor oBColor = new BackgroundColor() { Indexed = (UInt32Value)64U };
            oPatternFill.Append(oFColor);
            oPatternFill.Append(oBColor);

            Fill fill1 = new Fill();
            Fill fill2 = new Fill();
            fill2.Append(oPatternFill);

            Fills fills = new Fills();      // <APENDING Fills>
            fills.Append(fill0);
            fills.Append(fill1);
            fills.Append(fill2);

            // <Borders>
            Border border0 = new Border();     // Defualt border

            Borders borders = new Borders();    // <APENDING Borders>
            borders.Append(border0);

            // <CellFormats>
            CellFormat cellformat0 = new CellFormat() { FontId = 0, FillId = 0, BorderId = 0 }; // Default style : Mandatory | Style ID =0

            CellFormat cellformat1 = new CellFormat() { FontId = 1, FillId = 2 };  // Style with Bold text ; Style ID = 1

            // <APENDING CellFormats>
            CellFormats cellformats = new CellFormats();
            cellformats.Append(cellformat0);
            cellformats.Append(cellformat1);

            // Append FONTS, FILLS , BORDERS & CellFormats to stylesheet <Preserve the ORDER>
            workbookstylesheet.Append(fonts);
            workbookstylesheet.Append(fills);
            workbookstylesheet.Append(borders);
            workbookstylesheet.Append(cellformats);

            // Finalize
            stylesheet.Stylesheet = workbookstylesheet;
            stylesheet.Stylesheet.Save();
        }

        #region => Log error / notification message <=

        private static void LogMessage(string message, string callingFunctionName)
        {
            try
            {
                using (SPSite site = new SPSite(ConfigurationManager.AppSettings["siteURL"]))
                {
                    using (SPWeb oWeb = site.OpenWeb())
                    {
                        SPList oList = oWeb.Lists.TryGetList(Constants.ExceptionLogs.NAME);
                        if (null != oList)
                        {
                            oWeb.AllowUnsafeUpdates = true;
                            SPListItem newLog = oList.Items.Add();
                            newLog[Constants.ExceptionLogs.COL_COMPONENT] = callingFunctionName;
                            newLog[Constants.ExceptionLogs.COL_DETAILS] = string.Format("Message -> {0} ", message);
                            newLog.Update();
                            oWeb.AllowUnsafeUpdates = false;
                        }
                    }
                }
            }
            catch (Exception ex) { }
        }

        #endregion
    }
}
