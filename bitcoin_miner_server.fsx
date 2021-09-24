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
    | PrintCoins of string*string
    | StopMining
let mutable count=0L //to keep track of the workers
let actorCount = System.Environment.ProcessorCount
let system = ActorSystem.Create("RemoteFSharp", configuration)
let mutable ref = null

//Print actor to print the input string and hash value
let PrintingActor (mailbox:Actor<_>)=
    let rec loop()=actor{
        let! msg = mailbox.Receive()
        match msg with
        | PrintCoins(hash, input) -> 
            ref <! "The input is = "+input+"    The hash value is "+ hash
            // printfn "The input is = %s   The hash value is %s" input hash
            
        | _ -> failwith "unknown message"
        return! loop()
    }
    loop()
let printRef = spawn system "PrintingActor" PrintingActor 

let isValidHash = fun (x:string,y:int) -> int64 ("0x" + x.[0..y-1]) = 0L

//Miner actor
let MinerActor (mailbox:Actor<_>)=
    let rec loop()=actor{
        let! msg = mailbox.Receive()
        match msg with 
        | MineCoins(zeroCount, actorId) ->  let hashObj = SHA256Managed.Create()
                                            for i in 100001 .. actorCount .. 200000 do
                                                let  input = "harinisrinivasan"+ string (i+actorId)
                                                let hashString = input 
                                                                |> System.Text.Encoding.ASCII.GetBytes 
                                                                |> hashObj.ComputeHash
                                                                |> Seq.map(fun x -> x.ToString("x2")) 
                                                                |> Seq.reduce(+)
                                                let isValidHash = int64 ("0x" + hashString.[0..zeroCount-1]) = 0L
                                                
                                                if isValidHash then printRef <! PrintCoins(hashString, input)
                                            
                                            mailbox.Sender() <! StopMining

        | _ -> printfn "Miner Actor received a wrong message"
    }
    loop()

let workersList=[for a in 1 .. actorCount do yield(spawn system ("Job" + (string a)) MinerActor)]

//Master Actor
let MasterActor (mailbox:Actor<_>) =
    let rec loop()=actor{
        let! msg = mailbox.Receive()
        match msg with 
        | StartMining(zeroCount) ->
                                printfn "In Dispatcher"
                                for i in 0 .. actorCount-1 do //distributing work to the workers
                                    workersList.Item(i) <! MineCoins(zeroCount, i) //sending message to worker

        | StopMining -> count <- count + 1L
                        if count = int64 actorCount then 
                            ref <! "ProcessingDone"
                                            
        | _ -> printfn "Master actor received a wrong message"
        return! loop()
    }
    loop()

let localDispatcherRef = spawn system "localDisp" MasterActor

let commlink = 
    spawn system "server"
    <| fun mailbox ->
        let rec loop() =
            actor {
                let! msg = mailbox.Receive()
                printfn "%s" msg 
                let command = (msg|>string).Split ','
                if command.[0].CompareTo("Job")=0 then
                    
                    localDispatcherRef <! StartMining(command.[2]|>int)
                    ref <- mailbox.Sender()

                return! loop() 
            }
        loop()

// let echoClient = system.ActorSelection(
//                             "akka.tcp://RemoteFSharp@localhost:8778/user/EchoServer")

// let task = echoClient <? "F#!"

// let response = Async.RunSynchronously (task, 1000)

// printfn "Reply from remote %s" (string(response))


system.WhenTerminated.Wait()

//system.Terminate()
