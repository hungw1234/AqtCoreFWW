using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace TestWebApp
{
    public partial class FileUploadHandler : System.Web.UI.Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            this.Page.Response.Clear();
            if (base.Request.Files["uploadfile"] != null)
            {
                this.Upload();
                
            }
            else if (base.Request.QueryString["download"] != null)
            {
                string filename = base.Server.MapPath("~/congvan.pdf");
                System.Web.HttpResponse response = System.Web.HttpContext.Current.Response;
                response.ClearContent();
                response.Clear();
                response.ContentType = "application/pdf";
                response.AddHeader("Content-Disposition", "attachment; filename=" + "testfilename.pdf" + ";");
                response.TransmitFile(filename);
                response.Flush();
            }
            this.Page.Response.End();
        }

        private void Upload()
        {
            try {
                var builder = new UriBuilder(Request.Url.Scheme, Request.Url.Host, Request.Url.Port);

                HttpPostedFile file = base.Request.Files["uploadfile"];
                string path = Path.GetFileName(file.FileName);
                string fileExt = Path.GetExtension(path).ToLower();
                string uploadFilename = string.Format("{0}.signed{1}", Path.GetFileNameWithoutExtension(path), fileExt);
                string str = string.Format("{0}Upload/{1}", builder.ToString(), uploadFilename);
                file.SaveAs(base.Server.MapPath("~/Upload/" + uploadFilename));
                this.Page.Response.Write("{\"Status\":true, \"Message\": \"\", \"FileName\": \"" + path + "\", \"FileServer\": \"" + str + "\"}");
            }
            catch (Exception ex)
            {
                this.Page.Response.Write("{\"Status\":false, \"Message\": \""+ ex.Message + "\", \"FileName\": \"\", \"FileServer\": \"\"}");
            }
        }
    }
}