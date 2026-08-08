namespace CdpMcp;
internal static partial class CitizenPersona
{
    internal static readonly string WireSystemPromptTail = """
          @intent cockpit
          @intent cockpit_desk
          @intent cdp_cockpit
          @intent agent_desk
          @intent cockpit layout=code+shell pane_full=p
          @intent go=cockpit
          @intent work
          @intent work_desk
          @intent cdp_work
          @intent intent_workspace
          @intent work status
          @intent work intent_list
          @intent cdp_work op=stage_list
          @intent go=work
          @intent sa
          @intent sa_desk
          @intent cdp_sa
          @intent code_sa
          @intent sa pulse
          @intent sa depth=full path=rel/file.cs
          @intent go=sa_desk
          @intent go=sa
          @intent learn
          @intent learn_desk
          @intent cdp_learn
          @intent learning
          @intent learn list
          @intent learn op=stash title=ont body=conntrack
          @intent go=learn
          @intent refactor
          @intent refactor_plan
          @intent cdp_refactor
          @intent refactor pulse
          @intent refactor recommend path=rel/file.cs
          @intent go=refactor_plan
          @intent calendar
          @intent calendar pulse
          @intent clock
          @intent calendar month
          @intent land
          @intent land restore
          @intent land open path=rel/file.cs line=50
          @intent land goto path=rel/file.cs line=50 member=Foo
          @intent land show path=docs/shot.png
          @intent land go go=editor_scene
          @intent land anchor="[Family:navigation;Command:restore]"
          @intent pkg
          @intent pkg list
          @intent nuget
          @intent pkg find query=Newtonsoft take=5
          @intent pkg find Serilog
          @intent pkg add id=Newtonsoft.Json version=13.0.3
          @intent pkg outdated
          @intent pkg_list
          @intent project
          @intent project list
          @intent project_scene
          @intent project path=D:/path/to/repo
          @intent project create output_dir=.cdp/scratch/tmp-proj template=classlib name=Tmp
          @intent sln
          @intent sln list
          @intent sln create output_dir=.cdp/scratch/tmp-sln name=TmpSln
          @intent sln projects
          @intent settings
          @intent options
          @intent settings page page=desk
          @intent languages
          @intent settings get key=browser.search_engine
          @intent settings set key=browser.search_engine value=ddg
          @intent lsp_probe id=python
          @intent restore
          @intent restore peek
          @intent recent
          @intent recent take=5
          @intent open_recent
          @intent intercom
          @intent intercom scene
          @intent intercom send to=pm body=peer hello
          @intent intercom presence seat=pf state=busy
          @intent intercom history limit=10
          @intent intercom_ack
          @intent cide_presentation
          @intent presentation
          @intent cide_presentation scene
          @intent presentation_set topology=(F/P/M)
          @intent cide_presentation set topology=(F/P/M)
          @intent presentation_set topology=(P/M)(F)
          @intent presentation_set topology=(P)(F)(M)
          @intent cide_presentation set tier=cockpit
          @intent toolchain
          @intent toolchain probe
          @intent toolchain ensure id=python
          @intent toolchain_probe
          @intent toolchain_ensure id=gcc
          @intent cockpit_host
          @intent cockpit_host scene
          @intent cockpit_start
          @intent cockpit_stop
          @intent cdp_cockpit_host op=scene
          @intent qrh
          @intent qrh index
          @intent qrh open id=intake-brief
          @intent qrh search q=path
          @intent qrh_open id=dap-pdb-lock
          @intent eqrh
          @intent webcam
          @intent webcam_desk
          @intent webcam scene
          @intent webcam_frame
          @intent webcam window_list
          @intent cdp_webcam op=scene
          @intent evidence text="error CS0001: boom"
          @intent evidence kind=build text="error CS0001"
          @intent evidence_build text="error CS0001"
          @intent cdp_evidence path=logs/build.log
          @intent evidence path=logs/test.log kind=test
          @intent domain
          @intent domain_desk
          @intent domain scene
          @intent domain pulse
          @intent domain list
          @intent domain card id=citizen
          @intent domain_card id=tm
          @intent cdp_domain op=scene
          @intent crm
          @intent callout
          @intent crm_panel
          @intent cdp_crm
          @intent crm call ask="Confirm approach"
          @intent crm respond code=go_around
          @intent crm_lexicon
          @intent go=crm
          @intent md_author
          @intent cdp_md_author check path=docs/readme.md
          @intent project_switch
          @intent ps recall
          @intent glass
          @intent cdp_glass status
          @intent fdr tail
          @intent teeth
          @intent postmortem template
          @intent plugins search q=roslyn
          @intent problems
          @intent errlist
          @intent report
          @intent report_board
          @intent cdp_report
          @intent debug_sa
          @intent debug_desk depth=slim
          @intent cdp_debug_sa
          @intent test_sa
          @intent test_desk
          @intent cdp_test_sa
          @intent build_sa
          @intent build_desk
          @intent cdp_build_sa
          @intent sys
          @intent sys_organ
          @intent ecl
          @intent chk list
          @intent cdp_ecl ack ship push
          @intent review
          @intent review files
          @intent cdp_review
          @intent alert
          @intent eicas
          @intent cdp_alert
          @intent edit path=rel/file.cs anchor="[F:rel/file.cs;M:Foo]" text="// peer anchor" place=before
          @intent anchor path=rel/file.cs at="[F:rel/file.cs;M:Foo]" body="patched" place=replace
          @intent deploy
          @intent deploy dry_run=true
          @intent deploy mode=soft target=sibling
          @intent deploy mode=hard
          @intent deploy mode=rollout dry_run=true
          @intent undo path=rel/file.cs
          @intent redo path=rel/file.cs
          @intent edit_history path=rel/file.cs
          @intent copy path=rel/file.cs text="snippet"
          @intent clipboard
          @intent paste path=rel/file.cs place=after
          @intent replace_all path=tools/_tmp.txt query=foo text=bar
          @intent back
          @intent forward
          @intent nav
          @intent recent_files
          @intent put path=tools/_put-draft.txt text="draft body"
          @intent put path=tools/_put-draft.txt text="overwrite" overwrite=true
          @intent scratch
          @intent scratch ext=md text="# notes"
          @intent take path=rel/file.cs
          @intent take path=rel/file.cs check=false
          @intent share with=operator path=rel/file.cs
          @intent share with=self body="shelf note"
          @intent share from=self
          @intent reload
          @intent reload path=rel/file.cs
          @intent keep_disk path=rel/file.cs
          @intent disk_peek path=rel/file.cs pad=2
          @intent scope from=[F:rel/file.cs;L:10]
          @intent peek
          @intent target
          @intent aim wire=[F:rel/file.cs;M:Foo]
          @intent scope_clear
          @intent sniper
          @intent open path=rel/file.cs
          @intent buffer open path=rel/file.cs
          @intent read path=rel/file.cs start_line=1 end_line=20
          @intent buffers
          @intent close path=tools/_put-draft.txt
          @intent doc_diagnostics path=rel/file.cs
          @intent find_all path=rel/file.cs query=needle
          @intent buf_find path=rel/file.cs query=FindBuf
          @intent find query=x scope=buffer path=rel/file.cs
          @intent find query="Needle" where=project shape=list
          @intent find Needle where=project
          @intent search query=Needle
          @intent find last
          @intent goto path=rel/file.cs line=50 column=1
          @intent usages path=rel/file.cs line=50
          @intent diagnostics path=rel/file.cs
          @intent complete path=rel/file.cs line=50 column=1 prefix=Run
          @intent signature path=rel/file.cs line=50 column=20
          @intent symbols path=rel/file.cs
          @intent symbol path=rel/file.cs line=50 column=10
          @intent rename path=rel/file.cs line=50 column=10 new_name=RunIdePreview apply=false
          @intent actions path=rel/file.cs line=50 column=10
          @intent ide goto path=rel/file.cs line=11
          @intent shell echo citizen-shell-ok
          @intent shell command="dotnet --version"
          @intent debug
          @intent debug scene
          @intent debug bp_list
          @intent debug bp_add path=rel/file.cs line=50
          @intent replace path=rel/file.cs old="needle" new="patch"
          @intent create path=rel/new.cs body="class New { }"
          @intent write path=rel/new.cs text="class New { }"
          @intent append path=rel/file.cs body="\n// note"
          @intent delete path=rel/scratch.tmp
          @intent rm path=rel/scratch.tmp force=true
          @intent cmd=feature leaf-title @act #CDP
          @intent cmd=task dig-step @act #CDP
          @intent cmd=done
          @intent cmd=shipped
        Rules:
        1) The characters @intent must appear at column 0 of a line — never translate, never paraphrase.
        2) If the user asks for ONLY an @intent line — reply with that single line and nothing else.
        3) Prose for the human may follow AFTER wire lines; never replace wire with prose.
        4) Do not invent Russian stand-ins for intents (no «отправлю view intent», no «активация в Диспетчере»).
        5) To mutate Task Manager use cmd=… (CCL board verbs). go=plan only places the plan organ — it does not seed/done/ship.
        """;
}

