#time "on"
#r "nuget: Akka.FSharp" 

open Akka.Actor
open Akka.FSharp
open System.Security.Cryptography

//command line arguments
let zeroCount = fsi.CommandLineArgs.[1] |> int

//Getting processor count
let actorCount = System.Environment.ProcessorCount
// let actorCount = 8

//Actor count
let mutable idleActorCount = 0

//creating an actor system
let system = ActorSystem.Create("FSharp")

let gatorId = "venkateshpramani"

//union of messages to an actor
type Command =
    | StartMining
    | MineCoins of int*int
    | PrintCoins of list<string>
    | StopMining

//Print actor to print the input string and hash value
let Printer (mailbox:Actor<_>)=
    let rec loop()=actor{
        let! msg = mailbox.Receive()
        match msg with
        | PrintCoins(result) -> 
            printfn "The input is = %s   The hash value is %s" result.[0] result.[1]
            
        | _ -> failwith "unknown message"
        return! loop()
    }
    loop()
let printRef = spawn system "Printer" Printer 

//Miner actor
let Miner (mailbox:Actor<_>)=
    let rec loop()=actor{
        let! msg = mailbox.Receive()
        match msg with 
        | MineCoins(zeroCount, actorId) ->  let hashObj = SHA256Managed.Create()
                                            for i in 0 .. actorCount .. 100000000 do
                                                let  input = gatorId + string (i+actorId)
                                                let hashString = input 
                                                                |> System.Text.Encoding.ASCII.GetBytes 
                                                                |> hashObj.ComputeHash
                                                                |> Seq.map(fun x -> x.ToString("x2")) 
                                                                |> Seq.reduce(+)
                                                let isValidHash = int64 ("0x" + hashString.[0..zeroCount-1]) = 0L
                                                
                                                if isValidHash then printRef <! PrintCoins([input; hashString])
                                            
                                            mailbox.Sender() <! StopMining

        | _ -> printfn "Miner Actor received a wrong message"
    }
    loop()

//Master Actor
let Master (mailbox:Actor<_>) =
    let rec loop()=actor{
        let! msg = mailbox.Receive()
        match msg with 
        | StartMining ->    let actorRefList = [for i in 1 .. actorCount do yield(spawn system ("Actor" + (string i)) Miner)] //creating workers
                            for i in 0 .. actorCount-1 do //distributing work to the workers
                                actorRefList.Item(i) <! MineCoins(zeroCount, i) //sending message to worker

        | StopMining ->     idleActorCount <- idleActorCount + 1
                            if idleActorCount = actorCount then 
                                mailbox.Context.System.Terminate() |> ignore
                                            
        | _ -> printfn "Master actor received a wrong message"
        return! loop()
    }
    loop()


//creating master actor
let actorRef = spawn system "Master" Master
actorRef <! StartMining

//waiting for boss actor to terminate the actor system
system.WhenTerminated.Wait()
