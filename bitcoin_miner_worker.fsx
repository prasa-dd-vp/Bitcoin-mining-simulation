#time "on"
#r "nuget: Akka.FSharp" 
#r "nuget: Akka.Remote"
open Akka.Actor
open Akka.Configuration
open Akka.FSharp
open System.Security.Cryptography

//Command line arguments
let serverip = fsi.CommandLineArgs.[1] |> string
let port = fsi.CommandLineArgs.[2] |>string

//Actor server address
let addr = "akka.tcp://RemoteFSharp@" + serverip + ":" + port + "/user/Master"

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
let masterRef =  system.ActorSelection(addr)

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
            masterRef <! "PrintCoins," + result
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
                                            for i in 0 .. actorCount .. 10000000 do
                                                let input = "venkateshpramani"+ string (i+actorId)
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
                            masterRef <! "WorkDone"
                                            
        | _ -> printfn "Master actor received a wrong message"
        return! loop()
    }
    loop()

let mutable remoteWorkDone = false
let Worker (mailbox:Actor<_>)= 
    let rec loop() =
        actor {
            let! msg = mailbox.Receive()
            printfn "%s" msg 
            let response = msg |> string
            let command = response.Split(',')
            match command.[0] with
            | "PingMaster" ->   masterRef <! "Available"
            | "StartMining" ->  let actorRef = spawn system "MasterActor" MasterActor
                                actorRef <! StartMining(int command.[1])
            return! loop() 
        }
    loop()
let commlink = spawn system "Worker" Worker

commlink <! "PingMaster"


system.WhenTerminated.Wait()

