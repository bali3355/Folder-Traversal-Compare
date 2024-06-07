using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace ComparsionEnumerations
{
    class AclChecker
    {
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool GetNamedSecurityInfo(
            string pObjectName,
            SE_OBJECT_TYPE ObjectType,
            out IntPtr pSecurityDescriptor
        );

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool GetEffectiveRightsFromAcl(
            IntPtr pSecurityDescriptor,
            IntPtr pTrustee,
            out uint pAccessMask
        );

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool IsValidSid(IntPtr pSid);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool LookupAccountName(
            string lpSystemName,
            string lpAccountName,
            IntPtr Sid,
            ref int cbSid,
            StringBuilder ReferencedDomainName,
            ref int cchReferencedDomainName,
            out SID_NAME_USE peUse
        );

        const int ERROR_INSUFFICIENT_BUFFER = 122;
        const int ERROR_NONE_MAPPED = 1332;

        enum SE_OBJECT_TYPE
        {
            SE_UNKNOWN_OBJECT_TYPE,
            SE_FILE_OBJECT,
            SE_SERVICE,
            SE_PRINTER,
            SE_REGISTRY_KEY,
            SE_LMSHARE,
            SE_KERNEL_OBJECT,
            SE_WINDOW_OBJECT,
            SE_DS_OBJECT,
            SE_DS_OBJECT_ALL,
            SE_PROVIDER_DEFINED_OBJECT,
            SE_WMIGUID_OBJECT,
            SE_REGISTRY_WOW64_32KEY
        }

        enum SID_NAME_USE
        {
            SidTypeUser = 1,
            SidTypeGroup,
            SidTypeDomain,
            SidTypeAlias,
            SidTypeWellKnownGroup,
            SidTypeDeletedAccount,
            SidTypeInvalid,
            SidTypeUnknown,
            SidTypeComputer
        }

        public static void GetAccessControlInfo(string path)
        {
            IntPtr pSecurityDescriptor;
            if (!GetNamedSecurityInfo(path, SE_OBJECT_TYPE.SE_FILE_OBJECT, out pSecurityDescriptor))
            {
                throw new Exception("Failed to get security descriptor");
            }

            IntPtr pTrustee = IntPtr.Zero;
            try
            {
                string accountName = "DOMAIN\\User"; // replace with the user/group you want to check
                int cbSid = 0;
                StringBuilder referencedDomainName = new StringBuilder();
                int cchReferencedDomainName = 256;
                SID_NAME_USE peUse;

                if (!LookupAccountName(null, accountName, IntPtr.Zero, ref cbSid, referencedDomainName, ref cchReferencedDomainName, out peUse))
                {
                    if (Marshal.GetLastWin32Error() == ERROR_INSUFFICIENT_BUFFER)
                    {
                        pTrustee = Marshal.AllocHGlobal(cbSid);
                        if (!LookupAccountName(null, accountName, pTrustee, ref cbSid, referencedDomainName, ref cchReferencedDomainName, out peUse))
                        {
                            throw new Exception("Failed to lookup account name");
                        }
                    }
                    else
                    {
                        throw new Exception("Failed to lookup account name");
                    }
                }

                uint accessMask;
                if (!GetEffectiveRightsFromAcl(pSecurityDescriptor, pTrustee, out accessMask))
                {
                    throw new Exception("Failed to get effective rights");
                }

                Console.WriteLine($"Access mask: {accessMask}");
            }
            finally
            {
                if (pTrustee != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(pTrustee);
                }
            }
        }
    }
}
