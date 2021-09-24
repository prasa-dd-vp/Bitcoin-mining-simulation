#time "on"
#r "nuget: Akka.FSharp" 
#r "nuget: Akka.Remote"
open Akka.Actor
open Akka.Configuration
open Akka.FSharp
open System.Security.Cryptography

//Command line arguments
let zeroCount = fsi.CommandLineArgs.[1] |> int
let serverip = fsi.CommandLineArgs.[2] |> string
let port = fsi.CommandLineArgs.[3] |>string

//Actor server address
let addr = "akka.tcp://RemoteFSharp@" + serverip + ":" + port + "/user/Server"

let mutable count=0
let actorCount = System.Environment.ProcessorCount

let configuration = 
    ConfigurationFactory.ParseString(
        @"akka {
            actor {
                provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                
            }
            remote {
                helios.tcp {
                    port = 8778
                    hostname = localhost
                }
            }
        }")

let system = ActorSystem.Create("ClientFsharp", configuration)

//union of messages to an actor
type ActorMsg =
    | StartMining of int
    | MineCoins of int*int
    | PrintCoins of string
    | StopMining

//Print actor to print the input string and hash value
let PrintingActor (mailbox:Actor<_>)=
    let rec loop()=actor{
        let! msg = mailbox.Receive()
        match msg with
        | PrintCoins(result) ->
            let resultArr = result.Split ',' 
            printfn "The input is = %s   The hash value is %s" resultArr.[0] resultArr.[1]
            
        | _ -> failwith "unknown message"
        return! loop()
    }
    loop()
let printRef = spawn system "PrintingActor" PrintingActor 

//Miner actor
let MinerActor (mailbox:Actor<_>)=
    let rec loop()=actor{
        let! msg = mailbox.Receive()
        match msg with 
        | MineCoins(zeroCount, actorId) ->  let hashObj = SHA256Managed.Create()
                                            for i in 0 .. actorCount .. 1000000 do
                                                let  input = "venkateshpramani"+ string (i+actorId)
                                                let hashString = input 
                                                                |> System.Text.Encoding.ASCII.GetBytes 
                                                                |> hashObj.ComputeHash
                                                                |> Seq.map(fun x -> x.ToString("x2")) 
                                                                |> Seq.reduce(+)
                                                let isValidHash = int64 ("0x" + hashString.[0..zeroCount-1]) = 0L
                                                
                                                if isValidHash then printRef <! PrintCoins(input+","+hashString)
                                            
                                            mailbox.Sender() <! StopMining

        | _ -> printfn "Miner Actor received a wrong message"
    }
    loop()

let mutable localWorkDone = false
//Master Actor
let MasterActor (mailbox:Actor<_>) =
    let rec loop()=actor{
        let! msg = mailbox.Receive()
        match msg with 
        | StartMining(zeroCount) ->
                                let actorRefList = [for i in 1 .. actorCount do yield(spawn system ("Actor" + (string i)) MinerActor)] //creating workers
                                for i in 0 .. actorCount-1 do //distributing work to the workers
                                    actorRefList.Item(i) <! MineCoins(zeroCount, i) //sending message to worker

        | StopMining -> count <- count + 1
                        if count = actorCount then 
                            localWorkDone <- true
                                            
        | _ -> printfn "Master actor received a wrong message"
        return! loop()
    }
    loop()

let mutable remoteWorkDone = false
let Client (mailbox:Actor<_>)= 
    let rec loop() =
        actor {
            let! msg = mailbox.Receive()
            // printfn "%s" msg 
            let response =msg|>string
            let command = (response).Split ','
            if command.[0].CompareTo("init")=0 then
                let echoClient = system.ActorSelection(addr)
                let msgToServer = "Job,harinisrinivasan," + (zeroCount|>string)
                echoClient <! msgToServer
                let actorRef = spawn system "MasterActor" MasterActor
                actorRef <! StartMining(zeroCount)
            elif response.CompareTo("ProcessingDone")=0 then
                system.Terminate() |> ignore
                remoteWorkDone <- true
            else
                printRef <! PrintCoins(msg)

            return! loop() 
        }
    loop()
let commlink = spawn system "Client" Client


commlink <! "init"


system.WhenTerminated.Wait()

