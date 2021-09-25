#time "on"
#r "nuget: Akka.FSharp" 
#r "nuget: Akka.Remote"

open System
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
                    port = 8777
                    hostname = 192.168.0.213
                }
            }
        }")

//union of commands to an actor
type Command =
    | StartMining of int
    | MineCoins of int*int
    | PrintCoins of string
    | StopMining

//Command line argument
let zeroCount = fsi.CommandLineArgs.[1] |> int

let mutable idleActorCount=0
let actorCount = System.Environment.ProcessorCount
let system = ActorSystem.Create("MainFSharp", configuration)
let gatorId = "harinisrinivasan"

//Print actor to print the mined coins
let Printer (mailbox:Actor<_>)=
    let rec loop()=actor{
        let! msg = mailbox.Receive()
        match msg with
        | PrintCoins(result) -> 
            let resultArr = result.Split(',')
            printfn "The input is = %s   The hash value is = %s" resultArr.[0] resultArr.[1]
            
        | _ -> failwith "unknown message"
        return! loop()
    }
    loop()
let printRef = spawn system "Printer" Printer 

//Miner actor to generate coins
let Miner (mailbox:Actor<_>)=
    let rec loop()=actor{
        let! msg = mailbox.Receive()
        match msg with 
        | MineCoins(zeroCount, actorId) ->  let hashObj = SHA256Managed.Create()
                                            for i in 1 .. actorCount .. 10000000 do
                                                let  input = gatorId + string (i+actorId)
                                                let hashString = input 
                                                                |> System.Text.Encoding.ASCII.GetBytes 
                                                                |> hashObj.ComputeHash
                                                                |> Seq.map(fun x -> x.ToString("x2")) 
                                                                |> Seq.reduce(+)
                                                let isValidHash = int64 ("0x" + hashString.[0..zeroCount-1]) = 0L
                                                
                                                if isValidHash then printRef <! PrintCoins(input+","+hashString)
                                            
                                            mailbox.Sender() <! StopMining

        | _ -> printfn "Miner Actor received a wrong message"
        return! loop()
    }
    loop()

//Initiator Actor
let Initiator (mailbox:Actor<_>) =
    let rec loop()=actor{
        let! msg = mailbox.Receive()
        match msg with 
        | StartMining(zeroCount) -> let actorRefList = [for a in 1 .. actorCount do yield(spawn system ("Actor" + (string a)) Miner)]
                                    for i in 0 .. actorCount-1 do //distributing work to the workers
                                        actorRefList.Item(i) <! MineCoins(zeroCount, i) //sending message to worker

        | StopMining             -> idleActorCount <- idleActorCount + 1
                                    if idleActorCount = actorCount then
                                        printfn "Workdone by master" 
                                        idleActorCount <- 0
                                            
        | _                      -> printfn "Master actor received a wrong message"
        return! loop()
    }
    loop()

//Master Actor
let Master (mailbox:Actor<_>) = 
    let rec loop() = actor {
        let! msg = mailbox.Receive()
        let command = (msg|>string).Split ','

        match command.[0] with
        | "StartMining" ->  let initiatorRef = spawn system "Initiator" Initiator
                            initiatorRef <! StartMining(zeroCount)
        
        | "Available"   ->  mailbox.Sender() <! "StartMining," + string zeroCount 
        
        | "PrintCoins"  ->  printRef <! PrintCoins(command.[1]+","+ command.[2])
        
        | "WorkDone"    ->  printfn "WorkDone by one of the worker"
                            // system.Terminate()
        
        | _             -> printfn "Master actor received a wrong message"
        return! loop() 
    }
    loop()

let masterRef = spawn system "Master" Master
masterRef <! "StartMining"

system.WhenTerminated.Wait()