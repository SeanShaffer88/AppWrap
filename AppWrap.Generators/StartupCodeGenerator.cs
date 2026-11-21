using AppWrap.Core;

namespace AppWrap.Generators;

// All integration is per-user. Reading a link never executes its target.
internal static class StartupCodeGenerator
{
    private static string Pascal(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    internal static string Generate(AppWrapInput input, StandardInstallerOptions options) => $$"""

        [Code]
        const
          StartupOffered = {{(options.OfferStartWithWindows ? "True" : "False")}};
          CurrentRelativeExe = '{{Pascal(input.MainExecutable)}}';
          CurrentRelativeCwd = '{{Pascal(Path.GetDirectoryName(input.MainExecutable) ?? "")}}';
          StartupUninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\AppWrap-{{InputRules.SafeId(input.AppName)}}_is1';
        var
          StartupChoice: TNewCheckBox;
          PriorRoot, PriorExe, PriorCwd: String;
          CleanupRoot, CleanupExe, CleanupCwd: String;
          PriorCaptured: Boolean;

        function StartupAttributes(Name: String): LongWord;
          external 'GetFileAttributesW@kernel32.dll stdcall';
        function StartupLastError: LongWord;
          external 'GetLastError@kernel32.dll stdcall';

        function PlainPath(Name: String): Boolean;
        var Attr, ErrorCode: LongWord; Parent: String; I: Integer;
        begin
          Result := False;
          if (Length(Name) < 3) or (Copy(Name, 2, 2) <> ':\') then exit;
          if not (((Name[1] >= 'A') and (Name[1] <= 'Z')) or ((Name[1] >= 'a') and (Name[1] <= 'z'))) then exit;
          if (Pos('/', Name) > 0) or (Trim(Name) <> Name) then exit;
          for I := 1 to Length(Name) do if Ord(Name[I]) < 32 then exit;
          if CompareText(RemoveBackslashUnlessRoot(ExpandFileName(Name)), RemoveBackslashUnlessRoot(Name)) <> 0 then exit;
          while Length(Name) > 3 do begin
            Attr := StartupAttributes(Name);
            if Attr = $FFFFFFFF then begin
              ErrorCode := StartupLastError;
              if (ErrorCode <> 2) and (ErrorCode <> 3) then exit;
            end else if (Attr and $400) <> 0 then exit;
            Parent := ExtractFileDir(Name);
            if CompareText(Parent, Name) = 0 then exit;
            Name := Parent;
          end;
          Result := True;
        end;

        function RelativePath(Name: String; AllowEmpty: Boolean): Boolean;
        var Parts: TArrayOfString; I, J: Integer;
        begin
          Result := AllowEmpty and (Name = '');
          if Name = '' then exit;
          if (Pos(':', Name) > 0) or (Pos('/', Name) > 0) or (Copy(Name, 1, 1) = '\') then exit;
          Parts := StringSplit(Name, ['\'], stAll);
          for I := 0 to GetArrayLength(Parts) - 1 do begin
            if (Parts[I] = '') or (Parts[I] = '.') or (Parts[I] = '..') or
              (Trim(Parts[I]) <> Parts[I]) or (Copy(Parts[I], Length(Parts[I]), 1) = '.') then exit;
            for J := 1 to Length(Parts[I]) do
              if (Ord(Parts[I][J]) < 32) or (Pos(Parts[I][J], '*?"<>|') > 0) then exit;
          end;
          Result := True;
        end;

        function ValidTuple(Root, Exe, Cwd: String): Boolean;
        var Prefix, Target, Work: String;
        begin
          Result := False;
          if not RelativePath(Exe, False) or not RelativePath(Cwd, True) then exit;
          if not PlainPath(Root) then exit;
          Root := RemoveBackslashUnlessRoot(ExpandFileName(Root));
          Prefix := AddBackslash(Root);
          Target := ExpandFileName(Prefix + Exe);
          Work := RemoveBackslashUnlessRoot(ExpandFileName(Prefix + Cwd));
          if CompareText(Copy(Target, 1, Length(Prefix)), Prefix) <> 0 then exit;
          if (CompareText(Work, Root) <> 0) and
            (CompareText(Copy(Work, 1, Length(Prefix)), Prefix) <> 0) then exit;
          Result := PlainPath(Target) and PlainPath(Work);
        end;

        function StartupName: String;
        begin
          Result := ExpandConstant('{userstartup}\AppWrap-{{InputRules.SafeId(input.AppName)}}.lnk');
        end;

        function LinkMatches(Root, Exe, Cwd: String): Boolean;
        var Shell, Link: Variant; Target, Work: String;
        begin
          Result := False;
          if not ValidTuple(Root, Exe, Cwd) or not PlainPath(StartupName) or not FileExists(StartupName) then exit;
          try
            Shell := CreateOleObject('WScript.Shell');
            Link := Shell.CreateShortcut(StartupName);
            Target := Link.TargetPath; Work := Link.WorkingDirectory;
            Result := (Target <> '') and (Work <> '') and (Link.Arguments = '') and
              (CompareText(ExpandFileName(Target), ExpandFileName(AddBackslash(Root) + Exe)) = 0) and
              (CompareText(RemoveBackslashUnlessRoot(ExpandFileName(Work)),
                RemoveBackslashUnlessRoot(ExpandFileName(AddBackslash(Root) + Cwd))) = 0);
          except
            Log('AppWrap: unreadable startup link preserved.');
          end;
        end;

        procedure CapturePriorStartup;
        var InstalledRoot: String;
        begin
          if PriorCaptured then exit;
          PriorCaptured := True;
          PriorRoot := GetPreviousData('AppWrapStartupRoot', '');
          PriorExe := GetPreviousData('AppWrapStartupExe', '');
          PriorCwd := GetPreviousData('AppWrapStartupCwd', '');
          RegQueryStringValue(HKCU, StartupUninstallKey, 'Inno Setup: App Path', InstalledRoot);
          if not ValidTuple(PriorRoot, PriorExe, PriorCwd) or
            (CompareText(RemoveBackslashUnlessRoot(PriorRoot), RemoveBackslashUnlessRoot(InstalledRoot)) <> 0) then begin
            PriorRoot := ''; PriorExe := ''; PriorCwd := '';
          end;
        end;

        function StartupLinkOwned: Boolean;
        begin
          if IsUninstaller then
            Result := LinkMatches(CleanupRoot, CleanupExe, CleanupCwd)
          else begin
            CapturePriorStartup;
            Result := LinkMatches(ExpandConstant('{app}'), CurrentRelativeExe, CurrentRelativeCwd) or
              LinkMatches(PriorRoot, PriorExe, PriorCwd);
          end;
          if FileExists(StartupName) and not Result then Log('AppWrap: foreign or malformed startup link preserved.');
        end;

        procedure WriteStartupLink;
        begin
          if not PlainPath(StartupName) or not ValidTuple(ExpandConstant('{app}'), CurrentRelativeExe, CurrentRelativeCwd) then
            RaiseException('The startup path contains a linked or invalid folder.');
          if FileExists(StartupName) and not StartupLinkOwned then
            RaiseException('A different startup shortcut already uses this name.');
          CreateShellLink(StartupName, '{{Pascal(input.AppName)}}',
            ExpandConstant('{app}\') + CurrentRelativeExe, '',
            ExpandConstant('{app}\') + CurrentRelativeCwd, '', 0, SW_SHOWNORMAL);
          if not LinkMatches(ExpandConstant('{app}'), CurrentRelativeExe, CurrentRelativeCwd) then
            RaiseException('The startup shortcut could not be verified.');
          if not RegWriteStringValue(HKCU, StartupUninstallKey, 'Inno Setup CodeFile: AppWrapStartupRoot', ExpandConstant('{app}')) or
            not RegWriteStringValue(HKCU, StartupUninstallKey, 'Inno Setup CodeFile: AppWrapStartupExe', CurrentRelativeExe) or
            not RegWriteStringValue(HKCU, StartupUninstallKey, 'Inno Setup CodeFile: AppWrapStartupCwd', CurrentRelativeCwd) then
            RaiseException('The startup shortcut ownership could not be saved.');
          Log('AppWrap: owned startup shortcut updated.');
        end;

        procedure InitializeWizard;
        begin
          CapturePriorStartup;
          if StartupOffered and not WizardSilent then begin
            StartupChoice := TNewCheckBox.Create(WizardForm);
            StartupChoice.Parent := WizardForm.FinishedPage;
            StartupChoice.Caption := 'Start with Windows (optional)';
            StartupChoice.Left := WizardForm.RunList.Left;
            StartupChoice.Width := WizardForm.RunList.Width;
            StartupChoice.Height := ScaleY(22);
          end;
        end;

        procedure CurPageChanged(CurPageID: Integer);
        var Rows: Integer;
        begin
          if (CurPageID = wpFinished) and Assigned(StartupChoice) then begin
            Rows := WizardForm.RunList.Items.Count;
            if Rows = 0 then begin
              StartupChoice.Top := WizardForm.RunList.Top;
              WizardForm.RunList.Visible := False;
            end else begin
              WizardForm.RunList.Height := Rows * ScaleY(26);
              StartupChoice.Top := WizardForm.RunList.Top + WizardForm.RunList.Height + ScaleY(4);
            end;
            StartupChoice.Checked := StartupLinkOwned;
            StartupChoice.Visible := True;
          end;
        end;

        function NextButtonClick(CurPageID: Integer): Boolean;
        begin
          Result := True;
          if (CurPageID <> wpFinished) or WizardSilent or not Assigned(StartupChoice) then exit;
          try
            if StartupChoice.Checked then WriteStartupLink
            else if StartupLinkOwned then begin
              if not DeleteFile(StartupName) then RaiseException('The startup shortcut could not be removed.');
              Log('AppWrap: owned startup shortcut removed.');
            end;
          except
            MsgBox('Could not update Start with Windows. Close apps using the startup shortcut and retry, or uncheck this option.' + #13#10 + GetExceptionMessage, mbError, MB_OK);
            Result := False;
          end;
        end;

        procedure CurStepChanged(CurStep: TSetupStep);
        begin
          if (CurStep = ssPostInstall) and (WizardSilent or not StartupOffered) then begin
            CapturePriorStartup;
            if LinkMatches(PriorRoot, PriorExe, PriorCwd) then begin
              try WriteStartupLink;
              except Log('AppWrap: existing startup shortcut migration failed: ' + GetExceptionMessage); end;
            end;
          end;
        end;

        procedure RegisterPreviousData(PreviousDataKey: Integer);
        begin
          CapturePriorStartup;
          { Retain the last-owned tuple if a migration failed or Finish is cancelled. }
          if LinkMatches(PriorRoot, PriorExe, PriorCwd) and
            not LinkMatches(ExpandConstant('{app}'), CurrentRelativeExe, CurrentRelativeCwd) then begin
            SetPreviousData(PreviousDataKey, 'AppWrapStartupRoot', PriorRoot);
            SetPreviousData(PreviousDataKey, 'AppWrapStartupExe', PriorExe);
            SetPreviousData(PreviousDataKey, 'AppWrapStartupCwd', PriorCwd);
          end else begin
            SetPreviousData(PreviousDataKey, 'AppWrapStartupRoot', ExpandConstant('{app}'));
            SetPreviousData(PreviousDataKey, 'AppWrapStartupExe', CurrentRelativeExe);
            SetPreviousData(PreviousDataKey, 'AppWrapStartupCwd', CurrentRelativeCwd);
          end;
        end;

        function InitializeUninstall: Boolean;
        begin
          { Cache before the engine removes its uninstall key. Never read late during cleanup. }
          CleanupRoot := GetPreviousData('AppWrapStartupRoot', '');
          CleanupExe := GetPreviousData('AppWrapStartupExe', '');
          CleanupCwd := GetPreviousData('AppWrapStartupCwd', '');
          if CompareText(RemoveBackslashUnlessRoot(CleanupRoot), RemoveBackslashUnlessRoot(ExpandConstant('{app}'))) <> 0 then
            CleanupRoot := '';
          Result := True;
        end;

        procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
        begin
          { Check at uninstall time, not a setup-time [UninstallDelete] condition. }
          if (CurUninstallStep = usUninstall) and StartupLinkOwned then begin
            if DeleteFile(StartupName) then Log('AppWrap: owned startup shortcut removed during uninstall.')
            else Log('AppWrap: owned startup shortcut could not be removed during uninstall.');
          end;
        end;
        """;
}
