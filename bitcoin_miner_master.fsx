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
                debug : {
                    receive : on
                    autoreceive : on
                    lifecycle : on
                    event-stream : on
                    unhandled : on
                }
            }
            remote {
                helios.tcp {
                    port = 8777
                    hostname = 192.168.0.213
                }
            }
        }")

type ActorMsg =
    | StartMining of int
    | MineCoins of int*int
    | PrintCoins of string
    | StopMining

let zeroCount = fsi.CommandLineArgs.[1] |> int

let mutable count=0L //to keep track of the workers
let actorCount = System.Environment.ProcessorCount
let system = ActorSystem.Create("RemoteFSharp", configuration)

//Print actor to print the input string and hash value
let PrintingActor (mailbox:Actor<_>)=
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
let printRef = spawn system "PrintingActor" PrintingActor 

// let isValidHash = fun (x:string,y:int) -> int64 ("0x" + x.[0..y-1]) = 0L

//Miner actor
let MinerActor (mailbox:Actor<_>)=
    let rec loop()=actor{
        let! msg = mailbox.Receive()
        match msg with 
        | MineCoins(zeroCount, actorId) ->  let hashObj = SHA256Managed.Create()
                                            for i in 1 .. actorCount .. 10000000 do
                                                let  input = "harinisrinivasan"+ string (i+actorId)
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

let workersList=[for a in 1 .. actorCount do yield(spawn system ("Job" + (string a)) MinerActor)]

//Master Actor
let DispatcherActor (mailbox:Actor<_>) =
    let rec loop()=actor{
        let! msg = mailbox.Receive()
        match msg with 
        | StartMining(zeroCount) ->
                                printfn "In Dispatcher"
                                for i in 0 .. actorCount-1 do //distributing work to the workers
                                    workersList.Item(i) <! MineCoins(zeroCount, i) //sending message to worker

        | StopMining -> count <- count + 1L
                        if count = int64 actorCount then 
                            count <- 0L
                                            
        | _ -> printfn "Master actor received a wrong message"
        return! loop()
    }
    loop()

let localDispatcherRef = spawn system "localDisp" DispatcherActor

let Master (mailbox:Actor<_>) = 
    let rec loop() =
        actor {
            let! msg = mailbox.Receive()
            // printfn "%s" msg 
            let command = (msg|>string).Split ','

            match command.[0] with
            | "StartMining" -> localDispatcherRef <! StartMining(zeroCount)
            | "Available" -> mailbox.Sender() <! "StartMining," + string zeroCount 
            | "PrintCoins" ->   printRef <! PrintCoins(command.[1]+","+ command.[2])
            | "WorkDone" -> system.Terminate()
            | _ -> printfn "Server actor received a wrong message"
            return! loop() 
        }
    loop()

let commlink = spawn system "Master" Master

commlink <! "StartMining"


system.WhenTerminated.Wait()

//system.Terminate()
