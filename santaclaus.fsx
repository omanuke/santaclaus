#if INTERACTIVE
#r "PresentationCore.dll"
#r "PresentationFramework.dll"
#r "WindowsBase.dll"
#r "System.Xaml.dll"
#endif

open System
open System.Windows
open System.Windows.Controls
open System.Windows.Data
open System.Windows.Markup
open System.IO
open System.Threading
open System.Windows.Media.Animation
open System.Windows.Media
type Actor<'a>=MailboxProcessor<'a>
//common
let rnd=new Random()  
//setting
let santaY,elfY,rdeerY=800,300,300
let elfX,rdeerX=20,1000
let elfW,rdeerW=600,600

let santaNum,elfNum,rdeerNum=7,30,60
let meetingNum=3
let deliveryNum=6
let meetingTime=4000
let deliveryTime=4000
let rdeerSelect=3
let elfHNum,rdeerHNum=4,4
//event for ui
type GoHomeReason=
  |MeetingEnd
  |DeliveringEnd
  |EatingEnd
  |Busy
  |Absent
  |Getout
type SantaState=
  |OnMeeting
  |OnDelivering
  |FreeTime
type Notify=
  |ElfVisitSanta of elf:int*santa:int
  |AcceptElf of elf:int*santa:int*ind:int
  |ElfGoHome of elf:int*reason:GoHomeReason
  |ReindeerVisitSanta of rdeer:int*santa:int
  |AcceptReindeer of rdeer:int*santa:int*ind:int
  |ReindeerEating of rdeer:int
  |ReindeerGoHome of rdeer:int*reason:GoHomeReason
  |SantaStateChanged of santa:int*SantaState
type SantaEvent=
  |ElfVisit of elf:Actor<ElfEvent>*elfId:int*AsyncReplyChannel<SantaState>
  |ReindeerVisit of rdeer:Actor<ReindeerEvent>*rdeerId:int*AsyncReplyChannel<SantaState>
  |EndMeeting
  |EndDelivering
and ElfEvent=ElfEndVisit of GoHomeReason
and ReindeerEvent=ReindeerEndVisit of GoHomeReason
//----------------------
let spawnSanta santaNum elfNum rdeerNum (notifyE:Event<_>)=
  let notify e=notifyE.Trigger e
  let santas=ref [||]:Actor<_>[] ref
  let mutable elfs=[||]//とりあえず残しとく
  let mutable rdeers=[||]
  //santa----------
  let santa id=fun (actor:Actor<_>)->
    let rec loop stat elfs rdeers=async{
      let accept newId newAccepts others (r:AsyncReplyChannel<_>) nEvtF limit getout startX onFreeNext onXNext=
        r.Reply FreeTime
        notify <|nEvtF(newId,id,List.length newAccepts)
        if List.length newAccepts<limit then onFreeNext
        else  startX()
              others|>List.iter(fun (o:Actor<_>)->o.Post<|getout)
              onXNext
      let toFreeTime xs evt=
        xs|>List.iter(fun (e:Actor<_>)->e.Post <|evt)
        notify<|SantaStateChanged(id,FreeTime)
        loop FreeTime [] []
      let! msg=actor.Receive()
      let next=msg|>function
        |ElfVisit (elf,elfId,r)->stat|>function
          |FreeTime-> //accept elf
            let elfs=elf::elfs
            accept elfId elfs rdeers r AcceptElf meetingNum (ReindeerEndVisit Getout) 
                    startMeeting (loop FreeTime elfs rdeers) (loop OnMeeting elfs [])
          |_->r.Reply stat
              loop stat elfs rdeers
        |ReindeerVisit(rdeer,rdeerId,r)->stat|>function
          |FreeTime-> //accept reindeer
            let rdeers=rdeer::rdeers
            accept rdeerId rdeers elfs r AcceptReindeer deliveryNum (ElfEndVisit Getout) 
                    startDelivering (loop FreeTime elfs rdeers) (loop OnDelivering [] rdeers)
          |_->r.Reply stat
              loop stat elfs rdeers
        |EndMeeting->   toFreeTime elfs (ElfEndVisit MeetingEnd)
        |EndDelivering->toFreeTime rdeers (ReindeerEndVisit DeliveringEnd)
      return! next
      }
    and startMeeting()=startX OnMeeting meetingTime EndMeeting
    and startDelivering()=startX OnDelivering deliveryTime EndDelivering
    and startX stat wait evt=
      async{notify <|SantaStateChanged(id,stat)
            do! Async.Sleep(wait)
            actor.Post evt
      }|>Async.Start
    loop FreeTime [] []
  //elf----------
  let elf id=fun (actor:Actor<_>)->
    let rec loop()=async{
      let notify_loop e=
        async{notify e
              do! Async.Sleep(1000+rnd.Next 2000)
              return! loop()}
      let nSanta=rnd.Next santaNum
      notify<|ElfVisitSanta (id,nSanta+1)
      do! Async.Sleep(1000)
      let! sStat= (!santas).[nSanta].PostAndAsyncReply(fun r->ElfVisit(actor,id,r))
      let next=sStat|>function
        |OnMeeting->    notify_loop<|ElfGoHome (id, Busy)
        |OnDelivering-> notify_loop<|ElfGoHome (id, Absent)
        |FreeTime->async{
          let! ElfEndVisit(reason)=actor.Receive()
          return! notify_loop<|ElfGoHome (id,reason)
        }
      return! next}
    loop()
  //rdeer----------
  let rdeer id=fun (actor:Actor<_>)->
    let rec loop()=async{
      let notify_loop e=
        async{notify e
              do! Async.Sleep(1000+rnd.Next 2000)
              return! loop()}
      let next=
        //eat kusa?
        if rnd.Next rdeerSelect=0 then notify_loop<|ReindeerEating id
        else async{
          let nSanta=rnd.Next santaNum
          notify<|ReindeerVisitSanta (id,nSanta+1)
          do! Async.Sleep(1000)
          let! sStat= (!santas).[nSanta].PostAndAsyncReply(fun r->ReindeerVisit(actor,id,r))
          let next=
            sStat|>function
            |OnMeeting->    notify_loop<|ReindeerGoHome (id, Busy)
            |OnDelivering-> notify_loop<|ReindeerGoHome (id, Absent)
            |FreeTime->async{
              let! ReindeerEndVisit(reason)=actor.Receive()
              return! notify_loop<|ReindeerGoHome (id,reason)
            }
          return! next}
      return! next
    }
    loop()
  santas:=[|1..santaNum|]|>Array.map (santa>>Actor<_>.Start)
  elfs<-[|1..elfNum|]|>Array.map (elf>>Actor<_>.Start)
  rdeers<-[|1..rdeerNum|]|>Array.map (rdeer>>Actor<_>.Start)
//UI-------------------
let path= __SOURCE_DIRECTORY__ 
let sr=new StreamReader(path+ @"\mainwindow.xaml")
let xaml=sr.ReadToEnd()
let w=XamlReader.Parse xaml:?>Window
let canvas=w.FindName("_canvas"):?>Canvas
let ctx=SynchronizationContext.Current
w.Width<-1300.
w.Height<-1080.
let santaS id s=sprintf "( \"・ω・゛):%d %s" id s
let elfS id s=sprintf "(´・ω・｀):%d %s" id s
let rdeerS id s=sprintf "(^´・ω・｀^):%d %s" id s
let santaMap,elfMap,rdeerMap=
  [1..santaNum]|>List.map(fun i->
    let t=new TextBox(Text=santaS i "")
    t.Foreground<-new SolidColorBrush(Colors.Red)
    let x=20+280*(i-1)
    i,(t,double x,double santaY))|>Map.ofList,
  [1..elfNum]|>List.map(fun i->
    let t=new TextBox(Text=elfS i "")
    let x=elfX+elfW/elfHNum*(i%elfHNum)
    let y=elfY+i/elfHNum*20
    i,(t,double x,double y))|>Map.ofList,
  [1..rdeerNum]|>List.map(fun i->
    let t=new TextBox(Text=rdeerS i "")
    t.Foreground<-new SolidColorBrush(Colors.Green)
    let x=rdeerX+rdeerW/rdeerHNum*(i%rdeerHNum)
    let y=rdeerY+i/rdeerHNum*20
    i,(t,double x,double y))|>Map.ofList
let ft,fx,fy=(fun (t,_,_)->t),(fun (_,x,_)->x),(fun (_,_,y)->y)
Seq.concat([santaMap;elfMap;rdeerMap])
|>Seq.iter(fun kv->
  let (t,x,y)=kv.Value
  Canvas.SetLeft(t,x)
  Canvas.SetTop(t,y)
  canvas.Children.Add(t)|>ignore
)
let moveTo (e:UIElement) s (x,y) dur=
  (e:?>TextBox).Text<-s
  let s=new System.Windows.Media.Animation.Storyboard()
  let toDblAnim v o=
    let anim=new DoubleAnimation(To=Nullable(v))
    anim.Duration<-Duration(TimeSpan.FromMilliseconds(dur))
    Storyboard.SetTargetProperty(anim,new PropertyPath(o))
    Storyboard.SetTarget(anim,e)
    anim
  s.Children.Add(toDblAnim x Canvas.LeftProperty)
  s.Children.Add(toDblAnim y Canvas.TopProperty)
  s.Begin()
let notify=new Event<Notify>()
let goHomeS=[ MeetingEnd,"終わったお";Busy,"忙しかったお";
              Absent,"居なかったお";Getout,"追い出されたお"
              DeliveringEnd,"終わったお";EatingEnd,"美味しかったお"]|>Map.ofList
let santaStateS=[ OnMeeting,"話し中だっちゃ";OnDelivering,"配達中だっちゃ"
                  FreeTime,"暇っちゃ"]|>Map.ofList
notify.Publish.Subscribe(fun n->
  ctx.Post((fun _->
    try
      n|>function
      |ElfVisitSanta (elf,santa)->moveTo (ft elfMap.[elf])(elfS elf "逝ってくるお") (fx santaMap.[santa],double santaY) 1000.
      |AcceptElf (elf,santa,ind)->moveTo (ft elfMap.[elf])(elfS elf "話し合うお") (fx santaMap.[santa],double <|santaY+20*(ind+1)) 400.
      |ElfGoHome (elf,reason)-> let t,x,y=elfMap.[elf]
                                moveTo t (elfS elf goHomeS.[reason]) (x,y) 1000.
      |ReindeerVisitSanta (rdeer,santa)->moveTo (ft rdeerMap.[rdeer])(rdeerS rdeer "逝ってくるお") (fx santaMap.[santa]+135.,double santaY) 1000.
      |AcceptReindeer (rdeer,santa,ind)->moveTo (ft rdeerMap.[rdeer])(rdeerS rdeer "運ぶお") (fx santaMap.[santa]+135.,double<|santaY+20* (ind+1)) 400.
      |ReindeerEating (rdeer)->moveTo (ft rdeerMap.[rdeer])(rdeerS rdeer "食べるお") (800.+rnd.NextDouble()*400.,rnd.NextDouble()*100.) 1000.
      |ReindeerGoHome (rdeer,reason)->let t,x,y=rdeerMap.[rdeer]
                                      moveTo t (rdeerS rdeer goHomeS.[reason]) (x,y) 1000.
      |SantaStateChanged(santa,state)-> let t,_,_=santaMap.[santa]
                                        t.Text<-santaS santa santaStateS.[state]
    with e->printfn "%A %A" n e)
    ,null) )
spawnSanta santaNum elfNum rdeerNum notify

w.ShowDialog()
