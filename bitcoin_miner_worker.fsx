#time "on"
#r "nuget: Akka.FSharp" 
#r "nuget: Akka.Remote"

open Akka.Actor
open Akka.Configuration
open Akka.FSharp
open System.Security.Cryptography

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

//Creating actor system
let system = ActorSystem.Create("WorkerFsharp", configuration)

//Command line arguments
let serverip = fsi.CommandLineArgs.[1] |> string
let port = fsi.CommandLineArgs.[2] |>string

let addr = "akka.tcp://MainFSharp@" + serverip + ":" + port + "/user/Master"
let masterRef =  system.ActorSelection(addr)

let mutable idleActorCount=0
let actorCount = System.Environment.ProcessorCount
let gatorId = "venkateshpramani"

//union of commands to an actor
type Command =
    | StartMining of int
    | MineCoins of int*int
    | RouteToMaster of string
    | StopMining

//Router actor to print the input string and hash value
let Router (mailbox:Actor<_>)=
    let rec loop()=actor{
        let! msg = mailbox.Receive()
        match msg with
        | RouteToMaster(result) ->  masterRef <! "PrintCoins," + result

        | _                     ->  failwith "unknown message"
        return! loop()
    }
    loop()
let routerRef = spawn system "Router" Router 

//Miner actor
let Miner (mailbox:Actor<_>)=
    let rec loop()=actor{
        let! msg = mailbox.Receive()
        match msg with 
        | MineCoins(zeroCount, actorId) ->  let hashObj = SHA256Managed.Create()
                                            for i in 0 .. actorCount .. 10000000 do
                                                let input = gatorId + string (i+actorId)
                                                let hashString = input 
                                                                |> System.Text.Encoding.ASCII.GetBytes 
                                                                |> hashObj.ComputeHash
                                                                |> Seq.map(fun x -> x.ToString("x2")) 
                                                                |> Seq.reduce(+)
                                                let isValidHash = int64 ("0x" + hashString.[0..zeroCount-1]) = 0L
                                                
                                                if isValidHash then routerRef <! RouteToMaster(input+","+hashString)
                                            
                                            mailbox.Sender() <! StopMining

        | _                             ->  printfn "Miner Actor received a wrong message"
    }
    loop()

//Initiator Actor
let Initiator (mailbox:Actor<_>) =
    let rec loop()=actor{
        let! msg = mailbox.Receive()
        match msg with 
        | StartMining(zeroCount) -> let actorRefList = [for i in 1 .. actorCount do yield(spawn system ("Actor" + (string i)) Miner)] //creating workers
                                    for i in 0 .. actorCount-1 do //distributing work to the workers
                                        actorRefList.Item(i) <! MineCoins(zeroCount, i) //sending message to worker

        | StopMining             -> idleActorCount <- idleActorCount + 1
                                    if idleActorCount = actorCount then 
                                        masterRef <! "WorkDone"
                                            
        | _                      -> printfn "Initiator actor received a wrong message"
        return! loop()
    }
    loop()

//Worker Actor
let Worker (mailbox:Actor<_>)= 
    let rec loop() = actor {
        let! msg = mailbox.Receive()
        let msgString = msg |> string
        let command = msgString.Split(',')
        
        match command.[0] with
        | "PingMaster"  ->  masterRef <! "Available"
        
        | "StartMining" ->  let actorRef = spawn system "Initiator" Initiator
                            actorRef <! StartMining(int command.[1])

        | _             ->  printfn "Wroker received a wrong message"  
        return! loop() 
    }
    loop()

let workerRef = spawn system "Worker" Worker
workerRef <! "PingMaster"

system.WhenTerminated.Wait()

