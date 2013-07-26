'
' Copyright (C) 2013 getMaNGOS <http://www.getMangos.co.uk>
'
' This program is free software; you can redistribute it and/or modify
' it under the terms of the GNU General Public License as published by
' the Free Software Foundation; either version 2 of the License, or
' (at your option) any later version.
'
' This program is distributed in the hope that it will be useful,
' but WITHOUT ANY WARRANTY; without even the implied warranty of
' MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
' GNU General Public License for more details.
'
' You should have received a copy of the GNU General Public License
' along with this program; if not, write to the Free Software
' Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
'
Imports System.Threading
Imports System.Net.Sockets
Imports System.Xml.Serialization
Imports System.IO
Imports System.Net
Imports System.Reflection
Imports System.Runtime.CompilerServices
Imports System.Collections.Generic
Imports mangosVB.Common.BaseWriter
Imports mangosVB.Common

Public Module WS_Main

    #Region "Global.Variables"
    'Players' containers
    Public CLIENTs As New Dictionary(Of UInteger, ClientClass)
    Public CHARACTERs As New Dictionary(Of ULong, CharacterObject)
    Public CHARACTERs_Lock As New ReaderWriterLock

    'Worlds containers
    Public WORLD_CREATUREs_Lock As New ReaderWriterLock
    Public WORLD_CREATUREs As New Dictionary(Of ULong, CreatureObject)
    Public WORLD_GAMEOBJECTs As New Dictionary(Of ULong, GameObjectObject)
    Public WORLD_CORPSEOBJECTs As New Dictionary(Of ULong, CorpseObject)
    Public WORLD_DYNAMICOBJECTs_Lock As New ReaderWriterLock
    Public WORLD_DYNAMICOBJECTs As New Dictionary(Of ULong, DynamicObjectObject)
    Public WORLD_ITEMs As New Dictionary(Of ULong, ItemObject)

    'Database's containers - READONLY
    Public ITEMDatabase As New Dictionary(Of Integer, ItemInfo)
    Public CREATURESDatabase As New Dictionary(Of ULong, CreatureInfo)
    Public GAMEOBJECTSDatabase As New Dictionary(Of ULong, GameObjectInfo)

    'Other
    Public ItemGUIDCounter As ULong = GUID_ITEM
    Public CreatureGUIDCounter As ULong = GUID_UNIT
    Public GameObjectsGUIDCounter As ULong = GUID_GAMEOBJECT
    Public CorpseGUIDCounter As ULong = GUID_CORPSE
    Public DynamicObjectsGUIDCounter As ULong = GUID_DYNAMICOBJECT

    'System Things...
    Public Log As New BaseWriter
    Public PacketHandlers As New Dictionary(Of OPCODES, HandlePacket)
    Public Rnd As New Random
    Delegate Sub HandlePacket(ByRef Packet As PacketClass, ByRef Client As ClientClass)

    'Scripting Support
    Public AreaTriggers As ScriptedObject
    Public AI As ScriptedObject
    'Public CharacterCreation As ScriptedObject

    Public WS As WorldServerClass

    Public Const SERVERSEED As Integer = &HDE133700

    #End Region
    #Region "Global.Config"
    Public Config As XMLConfigFile
    
    <XmlRoot(ElementName:="WorldServer")> _
    Public Class XMLConfigFile
        <XmlElement(ElementName:="ServerLimit")> Public ServerLimit As Integer = 10
        <XmlElement(ElementName:="XPRate")> Public XPRate As Single = 1.0
        <XmlElement(ElementName:="ManaRegenerationRate")> Public ManaRegenerationRate As Single = 1.0
        <XmlElement(ElementName:="HealthRegenerationRate")> Public HealthRegenerationRate As Single = 1.0
        <XmlElement(ElementName:="GlobalAuction")> Public GlobalAuction As Boolean = False
        <XmlElement(ElementName:="SaveTimer")> Public SaveTimer As Integer = 120000

        <XmlElement(ElementName:="LogType")> Public LogType As String = "COLORCONSOLE"
        <XmlElement(ElementName:="LogLevel")> Public LogLevel As LogType = mangosVB.Common.BaseWriter.LogType.NETWORK
        <XmlElement(ElementName:="LogConfig")> Public LogConfig As String = ""

        <XmlElement(ElementName:="AccountDatabase")> Public AccountDatabase As String = "root;password;localhost;3306;mvb_dev;MySQL"
        <XmlElement(ElementName:="CharacterDatabase")> Public CharacterDatabase As String = "root;password;localhost;3306;mvb_dev;MySQL"
        <XmlElement(ElementName:="WorldDatabase")> Public WorldDatabase As String = "root;password;localhost;3306;mvb_dev;MySQL"

        <XmlArray(ElementName:="ScriptsCompiler"), XmlArrayItem(GetType(String), ElementName:="Include")> Public CompilerInclude As New ArrayList
        <XmlArray(ElementName:="HandledMaps"), XmlArrayItem(GetType(String), ElementName:="Map")> Public Maps As New ArrayList

        <XmlElement(ElementName:="CreatePartyInstances")> Public CreatePartyInstances As Boolean = False
        <XmlElement(ElementName:="CreateRaidInstances")> Public CreateRaidInstances As Boolean = False
        <XmlElement(ElementName:="CreateBattlegrounds")> Public CreateBattlegrounds As Boolean = False
        <XmlElement(ElementName:="CreateArenas")> Public CreateArenas As Boolean = False
        <XmlElement(ElementName:="CreateOther")> Public CreateOther As Boolean = False

        <XmlElement(ElementName:="ClusterConnectMethod")> Public ClusterMethod As String = "tcp"
        <XmlElement(ElementName:="ClusterConnectHost")> Public ClusterHost As String = "localhost"
        <XmlElement(ElementName:="ClusterConnectPort")> Public ClusterPort As Integer = 50001
        <XmlElement(ElementName:="LocalConnectHost")> Public LocalHost As String = "localhost"
        <XmlElement(ElementName:="LocalConnectPort")> Public LocalPort As Integer = 50002
    End Class

    Public Sub LoadConfig()
        Try
            Dim FileName As String = "WorldServer.ini"

            'Get filename from console arguments
            Dim args As String() = Environment.GetCommandLineArgs()
            For Each arg As String In args
                If arg.IndexOf("config") <> -1 Then
                    FileName = Trim(arg.Substring(arg.IndexOf("=") + 1))
                End If
            Next
            'Make sure a config file exists
            If System.IO.File.Exists(FileName) = False Then
                Console.ForegroundColor = ConsoleColor.Red
                Console.WriteLine("[{0}] Cannot Continue. {1} does not exist.", Format(TimeOfDay, "hh:mm:ss"), FileName)
                Console.WriteLine("Please copy the ini files into the same directory as the Server exe files.")
                Console.WriteLine("Press any key to exit server: ")
                Console.ReadKey()
                End
            End If
            'Load config
            Console.Write("[{0}] Loading Configuration from {1}...", Format(TimeOfDay, "hh:mm:ss"), FileName)

            Config = New XMLConfigFile
            Console.Write("...")

            Dim oXS As XmlSerializer = New XmlSerializer(GetType(XMLConfigFile))

            Console.Write("...")
            Dim oStmR As StreamReader
            oStmR = New StreamReader(FileName)
            Config = oXS.Deserialize(oStmR)
            oStmR.Close()

            Console.WriteLine(".[done]")

            'DONE: Setting SQL Connections
            Dim AccountDBSettings() As String = Split(Config.AccountDatabase, ";")
            If AccountDBSettings.Length = 6 Then
                AccountDatabase.SQLDBName = AccountDBSettings(4)
                AccountDatabase.SQLHost = AccountDBSettings(2)
                AccountDatabase.SQLPort = AccountDBSettings(3)
                AccountDatabase.SQLUser = AccountDBSettings(0)
                AccountDatabase.SQLPass = AccountDBSettings(1)
                AccountDatabase.SQLTypeServer = CType([Enum].Parse(GetType(SQL.DB_Type), AccountDBSettings(5)), SQL.DB_Type)
            Else
                Console.WriteLine("Invalid connect string for the account database!")
            End If

            Dim CharacterDBSettings() As String = Split(Config.CharacterDatabase, ";")
            If CharacterDBSettings.Length = 6 Then
                CharacterDatabase.SQLDBName = CharacterDBSettings(4)
                CharacterDatabase.SQLHost = CharacterDBSettings(2)
                CharacterDatabase.SQLPort = CharacterDBSettings(3)
                CharacterDatabase.SQLUser = CharacterDBSettings(0)
                CharacterDatabase.SQLPass = CharacterDBSettings(1)
                CharacterDatabase.SQLTypeServer = CType([Enum].Parse(GetType(SQL.DB_Type), CharacterDBSettings(5)), SQL.DB_Type)
            Else
                Console.WriteLine("Invalid connect string for the character database!")
            End If

            Dim WorldDBSettings() As String = Split(Config.WorldDatabase, ";")
            If WorldDBSettings.Length = 6 Then
                WorldDatabase.SQLDBName = WorldDBSettings(4)
                WorldDatabase.SQLHost = WorldDBSettings(2)
                WorldDatabase.SQLPort = WorldDBSettings(3)
                WorldDatabase.SQLUser = WorldDBSettings(0)
                WorldDatabase.SQLPass = WorldDBSettings(1)
                WorldDatabase.SQLTypeServer = CType([Enum].Parse(GetType(SQL.DB_Type), WorldDBSettings(5)), SQL.DB_Type)
            Else
                Console.WriteLine("Invalid connect string for the world database!")
            End If

            'DONE: Creating logger
            Common.BaseWriter.CreateLog(Config.LogType, Config.LogConfig, Log)
            Log.LogLevel = Config.LogLevel

        Catch e As Exception
            Console.WriteLine(e.ToString)
        End Try
    End Sub
    #End Region

    #Region "WS.DataAccess"
    Public AccountDatabase As New SQL
    Public CharacterDatabase As New SQL
    Public WorldDatabase As New SQL
    Public Sub AccountSQLEventHandler(ByVal MessageID As SQL.EMessages, ByVal OutBuf As String)
        Select MessageID
            Case SQL.EMessages.ID_Error
                'Log.WriteLine(LogType.FAILED, OutBuf)
                Log.WriteLine(LogType.FAILED, "[ACCOUNT] " & OutBuf)
            Case SQL.EMessages.ID_Message
                'Log.WriteLine(LogType.SUCCESS, OutBuf)
                Log.WriteLine(LogType.SUCCESS, "[ACCOUNT] " & OutBuf)
        End Select
    End Sub
    Public Sub CharacterSQLEventHandler(ByVal MessageID As SQL.EMessages, ByVal OutBuf As String)
        Select MessageID
            Case SQL.EMessages.ID_Error
                Log.WriteLine(LogType.FAILED, "[CHARACTER] " & OutBuf)
            Case SQL.EMessages.ID_Message
                Log.WriteLine(LogType.SUCCESS, "[CHARACTER] " & OutBuf)
        End Select
    End Sub
    Public Sub WorldSQLEventHandler(ByVal MessageID As SQL.EMessages, ByVal OutBuf As String)
        Select Case MessageID
            Case SQL.EMessages.ID_Error
                Log.WriteLine(LogType.FAILED, "[WORLD] " & OutBuf)
            Case SQL.EMessages.ID_Message
                Log.WriteLine(LogType.SUCCESS, "[WORLD] " & OutBuf)
        End Select
    End Sub
    #End Region

    <System.MTAThreadAttribute()> _
    Sub Main()
        timeBeginPeriod(1)  'Set timeGetTime to a accuracy of 1ms

        Console.BackgroundColor = System.ConsoleColor.Black
        Console.Title = String.Format("{0} v{1}", CType([Assembly].GetExecutingAssembly().GetCustomAttributes(GetType(AssemblyTitleAttribute), False)(0), AssemblyTitleAttribute).Title, [Assembly].GetExecutingAssembly().GetName().Version)

        Console.ForegroundColor = System.ConsoleColor.Yellow

        Console.WriteLine(" ####       ####            ###     ###   ########    #######     ######## ")
        Console.WriteLine(" #####     #####            ####    ###  ##########  #########   ##########")
        Console.WriteLine(" #####     #####            #####   ###  ##########  #########   ##########")
        Console.WriteLine(" ######   ######            #####   ###  ###        ####   ####  ###       ")
        Console.WriteLine(" ######   ######    ####    ######  ###  ###        ###     ###  ###       ")
        Console.WriteLine(" ####### #######   ######   ######  ###  ###  ##### ###     ###  ########  ")
        Console.WriteLine(" ### ### ### ###   ######   ####### ###  ###  ##### ###     ###  ######### ")
        Console.WriteLine(" ### ### ### ###  ###  ###  ### ### ###  ###  ##### ###     ###   #########")
        Console.WriteLine(" ### ####### ###  ###  ###  ###  ######  ###    ### ###     ###        ####")
        Console.WriteLine(" ### ####### ###  ###  ###  ###  ######  ###    ### ###     ###         ###")
        Console.WriteLine(" ###  #####  ### ########## ###   #####  ###   #### ####   ####        ####")
        Console.WriteLine(" ###  #####  ### ########## ###   #####  #########   #########   ##########")
        Console.WriteLine(" ###  #####  ### ###    ### ###    ####  #########   #########   ######### ")
        Console.WriteLine(" ###   ###   ### ###    ### ###     ###   #######     #######     #######  ")
        Console.WriteLine("")
        Console.WriteLine(" Website: http://www.getmangos.co.uk                         ##  ##  ##### ")
        Console.WriteLine("                                                             ##  ##  ##  ##")
        Console.WriteLine("    Wiki: http://github.com/mangoswiki/wiki                  ##  ##  ##### ")
        Console.WriteLine("                                                              ####   ##  ##")
        Console.WriteLine("   Forum: http://community.getmangos.co.uk                     ##    ##### ")
        Console.WriteLine("")

        'Console.WriteLine("{0}", CType([Assembly].GetExecutingAssembly().GetCustomAttributes(GetType(AssemblyProductAttribute), False)(0), AssemblyProductAttribute).Product)
        'Console.WriteLine(CType([Assembly].GetExecutingAssembly().GetCustomAttributes(GetType(AssemblyCopyrightAttribute), False)(0), AssemblyCopyrightAttribute).Copyright)
        'Console.WriteLine()


        Console.ForegroundColor = System.ConsoleColor.White
        Console.Write(CType([Assembly].GetExecutingAssembly().GetCustomAttributes(GetType(System.Reflection.AssemblyTitleAttribute), False)(0), AssemblyTitleAttribute).Title)
        Console.WriteLine(" version {0}", [Assembly].GetExecutingAssembly().GetName().Version)
        Console.ForegroundColor = System.ConsoleColor.White

        Console.WriteLine("")
        Console.ForegroundColor = System.ConsoleColor.Gray

        Dim dateTimeStarted As Date = Now
        Log.WriteLine(LogType.INFORMATION, "[{0}] World Server Starting...", Format(TimeOfDay, "hh:mm:ss"))

        Dim currentDomain As AppDomain = AppDomain.CurrentDomain
        AddHandler currentDomain.UnhandledException, AddressOf GenericExceptionHandler

        LoadConfig()
        Console.ForegroundColor = System.ConsoleColor.Gray
        AddHandler AccountDatabase.SQLMessage, AddressOf AccountSQLEventHandler
        AddHandler CharacterDatabase.SQLMessage, AddressOf CharacterSQLEventHandler
        AddHandler WorldDatabase.SQLMessage, AddressOf WorldSQLEventHandler

        Dim ReturnValues As Integer
        ReturnValues = AccountDatabase.Connect()
        If ReturnValues > Common.SQL.ReturnState.Success Then   'Ok, An error occurred
            Console.WriteLine("[{0}] An SQL Error has occurred", Format(TimeOfDay, "hh:mm:ss"))
            Console.WriteLine("*************************")
            Console.WriteLine("* Press any key to exit *")
            Console.WriteLine("*************************")
            Console.ReadKey()
            End
        End If
        AccountDatabase.Update("SET NAMES 'utf8';")

        ReturnValues = CharacterDatabase.Connect()
        If ReturnValues > Common.SQL.ReturnState.Success Then   'Ok, An error occurred
            Console.WriteLine("[{0}] An SQL Error has occurred", Format(TimeOfDay, "hh:mm:ss"))
            Console.WriteLine("*************************")
            Console.WriteLine("* Press any key to exit *")
            Console.WriteLine("*************************")
            Console.ReadKey()
            End
        End If
        CharacterDatabase.Update("SET NAMES 'utf8';")

        ReturnValues = WorldDatabase.Connect()
        If ReturnValues > Common.SQL.ReturnState.Success Then   'Ok, An error occurred
            Console.WriteLine("[{0}] An SQL Error has occurred", Format(TimeOfDay, "hh:mm:ss"))
            Console.WriteLine("*************************")
            Console.WriteLine("* Press any key to exit *")
            Console.WriteLine("*************************")
            Console.ReadKey()
            End
        End If
        WorldDatabase.Update("SET NAMES 'utf8';")

        #If DEBUG Then
        Console.ForegroundColor = System.ConsoleColor.White
        Log.WriteLine(LogType.INFORMATION, "Running from: {0}", System.AppDomain.CurrentDomain.BaseDirectory)
        Console.ForegroundColor = System.ConsoleColor.Gray
        Log.WriteLine(LogType.DEBUG, "Setting MySQL into debug mode..[done]")
        AccountDatabase.Update("SET SESSION sql_mode='STRICT_ALL_TABLES';")
        CharacterDatabase.Update("SET SESSION sql_mode='STRICT_ALL_TABLES';")
        WorldDatabase.Update("SET SESSION sql_mode='STRICT_ALL_TABLES';")
        #End If
        InitializeInternalDatabase()
        IntializePacketHandlers()

        '        System.Diagnostics.Process.Start("RealmServer.exe")
        '        System.Diagnostics.Process.Start("Worldcluster.exe")


        WS = New WorldServerClass
        GC.Collect()

        If Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High Then
            Log.WriteLine(LogType.WARNING, "Setting Process Priority to HIGH..[done]")
        Else
            Log.WriteLine(LogType.WARNING, "Setting Process Priority to NORMAL..[done]")
        End If

        Log.WriteLine(LogType.INFORMATION, " Load Time:   {0}", Format(DateDiff(DateInterval.Second, dateTimeStarted, Now), "0 seconds"))
        Log.WriteLine(LogType.INFORMATION, " Used Memory: {0}", Format(GC.GetTotalMemory(False), "### ### ##0 bytes"))

        WaitConsoleCommand()

        Regenerator.Dispose()
        AreaTriggers.Dispose()
    End Sub

    Public Sub WaitConsoleCommand()
        Dim tmp As String = "", CommandList() As String, cmds() As String
        Dim cmd() As String = {}
        Dim varList As Integer
        While Not WS._flagStopListen
            Try
                tmp = Log.ReadLine()
                CommandList = tmp.Split(";")

                For varList = LBound(CommandList) To UBound(CommandList)
                    cmds = Split(CommandList(varList), " ", 2)
                    If CommandList(varList).Length > 0 Then
                        '<<<<<<<<<<<COMMAND STRUCTURE>>>>>>>>>>
                        Select Case cmds(0).ToLower
                            Case "quit", "shutdown", "off", "kill", "/quit", "/shutdown", "/off", "/kill"
                                Log.WriteLine(LogType.WARNING, "Server shutting down...")
                                WS._flagStopListen = True
                            Case "debug", "/debug"
                                Dim conn As New ClientClass
                                conn.DEBUG_CONNECTION = True
                                Dim test As New CharacterObject(conn, CType(cmds(1), Long))
                                CHARACTERs(CType(cmds(1), Long)) = test
                                AddToWorld(test)
                                Log.WriteLine(LogType.DEBUG, "Spawned character " & test.Name)
                            Case "createaccount", "/createaccount"
                                AccountDatabase.InsertSQL([String].Format("INSERT INTO accounts (account, password, email, joindate, last_ip) VALUES ('{0}', '{1}', '{2}', '{3}', '{4}')", cmd(1), cmd(2), cmd(3), Format(Now, "yyyy-MM-dd"), "0.0.0.0"))
                                If AccountDatabase.QuerySQL("SELECT * FROM accounts WHERE account = "" & packet_account & "";") Then
                                    Console.ForegroundColor = System.ConsoleColor.DarkGreen
                                    Console.WriteLine("[Account: " & cmd(1) & " Password: " & cmd(2) & " Email: " & cmd(3) & "] has been created.")
                                    Console.ForegroundColor = System.ConsoleColor.Gray
                                Else
                                    Console.ForegroundColor = System.ConsoleColor.DarkRed
                                    Console.WriteLine("[Account: " & cmd(1) & " Password: " & cmd(2) & " Email: " & cmd(3) & "] could not be created.")
                                    Console.ForegroundColor = System.ConsoleColor.Gray
                                End If
                            Case "exec", "/exec"
                                Dim tmpScript As New ScriptedObject("scripts\commands\" & cmds(1), "", True)
                                tmpScript.Invoke("CustomCommands", "OnExecute")
                                tmpScript.Dispose()
                            Case "gccollect"
                                GC.Collect()
                            Case "ban", "Ban", "Ban Account", "acct ban"
                                Console.ForegroundColor = System.ConsoleColor.DarkYellow
                                Console.WriteLine("[{0}] Specify the Account Name :", Format(TimeOfDay, "hh:mm:ss"))
                                Dim aName As String
                                aName = Console.ReadLine
                                Dim result As New DataTable
                                Dim result1 As New DataTable
                                AccountDatabase.Query("SELECT banned FROM accounts WHERE account = """ & aName & """;", result)
                                AccountDatabase.Query("SELECT last_ip FROM accounts WHERE account = """ & aName & """;", result1)
                                Dim IP As String
                                IP = result1.Rows(0).Item("last_ip")
                                If result.Rows.Count > 0 Then
                                    If result.Rows(0).Item("banned") = 1 Then
                                        Console.ForegroundColor = System.ConsoleColor.Green
                                        Console.WriteLine(String.Format("[{1}] Account [{0}] is already banned.", aName, Format(TimeOfDay, "hh:mm:ss")))

                                        Console.ForegroundColor = System.ConsoleColor.Yellow

                                    ElseIf IP = "127.0.0.1" Then
                                        Console.WriteLine("[{1}] Account [{0}] has the same IP Adress as the host.", aName, Format(TimeOfDay, "hh:mm:ss"))
                                    ElseIf IP = "0.0.0.0" Then
                                        Console.WriteLine("[{1}] Account [{0}] does not have an IP Address.", aName, Format(TimeOfDay, "hh:mm:ss"))
                                    Else
                                        AccountDatabase.Query("SELECT last_ip FROM accounts WHERE account = """ & aName & """;", result)
                                        Dim IP1 As String
                                        IP1 = result.Rows(0).Item("last_ip")
                                        AccountDatabase.Update("UPDATE accounts SET banned = 1 WHERE account = """ & aName & """;")
                                        Console.WriteLine(String.Format("[{1}] IP Address [{0}] is now banned.", IP, Format(TimeOfDay, "hh:mm:ss")))
                                        AccountDatabase.Update(String.Format("INSERT INTO `bans` VALUES ('{0}', '{1}', '{2}', '{3}');", IP1, Format(Now, "yyyy-MM-dd"), "No Reason Specified.", aName))
                                        Console.WriteLine(String.Format("[{1}] Account [{0}] is now banned.", aName, Format(TimeOfDay, "hh:mm:ss")))
                                    End If
                                Else
                                    Console.ForegroundColor = System.ConsoleColor.DarkGray
                                    Console.WriteLine(String.Format("[{1}] Account [{0}] is not found.", aName, Format(TimeOfDay, "hh:mm:ss")))
                                End If
                            Case "unban", "Unban", "UnBan", "acct unban", "Unban Account"
                                Console.ForegroundColor = System.ConsoleColor.DarkCyan
                                Console.WriteLine("[{0}] Account Name?", Format(TimeOfDay, "hh:mm:ss"))
                                Dim aName As String
                                Console.ForegroundColor = System.ConsoleColor.Magenta
                                aName = Console.ReadLine
                                Dim result As New DataTable
                                AccountDatabase.Query("SELECT banned FROM accounts WHERE account = """ & aName & """;", result)

                                If result.Rows.Count > 0 Then
                                    If result.Rows(0).Item("banned") = 0 Then
                                        Console.ForegroundColor = System.ConsoleColor.Green
                                        Console.WriteLine(String.Format("[{1}] Account [{0}] is not banned.", aName, Format(TimeOfDay, "hh:mm:ss")))
                                    Else
                                        Console.ForegroundColor = System.ConsoleColor.Red
                                        AccountDatabase.Update("UPDATE accounts SET banned = 0 WHERE account = """ & aName & """;")
                                        AccountDatabase.Query("SELECT last_ip FROM accounts WHERE account = """ & aName & """;", result)
                                        Dim IP As String
                                        IP = result.Rows(0).Item("last_ip")
                                        AccountDatabase.Update([String].Format("DELETE FROM `bans` WHERE `ip` = '{0}';", IP))
                                        Console.WriteLine(String.Format("[{1}] Account [{0}] has been unbanned.", aName, Format(TimeOfDay, "hh:mm:ss")))

                                    End If
                                Else
                                    Console.WriteLine(String.Format("[{1}] Account [{0}] is not found.", aName, Format(TimeOfDay, "hh:mm:ss")))
                                End If
                            Case "info", "/info"
                                Log.WriteLine(LogType.INFORMATION, "Used memory: {0}", Format(GC.GetTotalMemory(False), "### ### ##0 bytes"))
                            Case "db.restart", "/db.restart"
                                AccountDatabase.Restart()
                            Case "db.run", "/db.run"
                                AccountDatabase.Update(cmds(1))
                            Case "help", "/help"
                                Console.ForegroundColor = System.ConsoleColor.Blue
                                Console.WriteLine("'WorldServer' Command list:")
                                Console.ForegroundColor = System.ConsoleColor.White
                                Console.WriteLine("---------------------------------")
                                Console.WriteLine("")
                                Console.WriteLine("")
                                Console.WriteLine("'help' or '/help' - Brings up the 'WorldServer' Command list (this).")
                                Console.WriteLine("")
                                Console.WriteLine("'createaccount <user> <password> <email>' or '/createaccount <user> <password> <email>' - Creates an account with the specified username <user>, password <password>, and email <email>.")
                                Console.WriteLine("")
                                Console.WriteLine("'debug' or '/debug' - Creates a 'test' character.")
                                Console.WriteLine("")
                                Console.WriteLine("'info' or '/info' - Brings up a context menu showing server information (such as memory used).")
                                Console.WriteLine("")
                                Console.WriteLine("'exec' or '/exec' - Executes script files.")
                                Console.WriteLine("")
                                Console.WriteLine("'db.restart' or '/db.restart' - Reloads the database.")
                                Console.WriteLine("")
                                Console.WriteLine("'db.run' or '/db.run' - Runs and updates database.")
                                Console.WriteLine("")
                                Console.WriteLine("'quit' or 'shutdown' or 'off' or 'kill' or 'exit' - Shutsdown 'WorldServer'.")
                                Console.WriteLine("")
                                Console.WriteLine("'ban' or 'Ban'- Adds a Ban and IP Ban on an account.")
                                Console.WriteLine("")
                                Console.WriteLine("'unban' or 'Unban'- Removes a Ban and IP Ban on an account.")
                            Case Else
                                Console.ForegroundColor = System.ConsoleColor.DarkRed
                                Console.WriteLine("Error! Cannot find specified command. Please type 'help' for information on 'mangosVB.WorldServer' console commands.")
                                Console.ForegroundColor = System.ConsoleColor.Gray
                        End Select
                        '<<<<<<<<<<</END COMMAND STRUCTURE>>>>>>>>>>>>
                    End If
                Next
            Catch e As Exception
                Log.WriteLine(LogType.FAILED, "Error executing command [{0}]. {2}{1}", Format(TimeOfDay, "hh:mm:ss"), tmp, e.ToString, vbNewLine)
            End Try
        End While
    End Sub

    Private Sub GenericExceptionHandler(ByVal sender As Object, ByVal e As UnhandledExceptionEventArgs)
        Dim EX As Exception
        EX = e.ExceptionObject

        Log.WriteLine(LogType.CRITICAL, EX.ToString & vbNewLine)
        Log.WriteLine(LogType.FAILED, "Unexpected error has occured. An 'Error-yyyy-mmm-d-h-mm.log' file has been created. Please post the file in the BUG SECTION at getMaNGOS.co.uk (http://www.getMangos.co.uk/community)!")

        Dim tw As TextWriter
        tw = New StreamWriter(New FileStream(String.Format("Error-{0}.log", Format(Now, "yyyy-MMM-d-H-mm")), FileMode.Create))
        tw.Write(EX.ToString)
        tw.Close()
    End Sub

End Module
