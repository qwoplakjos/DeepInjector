using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace DeepInjector.Services
{

    public class FileAccessService
    {
        public void SetAccessControl(string filePath, string sidString = "S-1-15-2-1")
        {
            var sid = new SecurityIdentifier(sidString);
            var fileSecurity = File.GetAccessControl(filePath);

            var accessRule = new FileSystemAccessRule(
                sid,
                FileSystemRights.ReadAndExecute,
                InheritanceFlags.None,           
                PropagationFlags.None,
                AccessControlType.Allow
            );


            fileSecurity.AddAccessRule(accessRule);
            File.SetAccessControl(filePath, fileSecurity);
        }
    }
}
