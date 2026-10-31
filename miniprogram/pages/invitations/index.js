const model=require('../../utils/invitations');
const command=require('../../utils/invitation-command');
const readScope=require('../../utils/read-scope');
const { actor }=require('../../utils/feedback');
const { isGuid }=require('../../utils/assessment');
const message=require('../../utils/messages');
const ask=content=>new Promise(resolve=>wx.showModal({title:'核对学校邀请',content,success:r=>resolve(!!r.confirm),fail:()=>resolve(false)}));
Page({
  data:{items:[],total:0,offset:0,includeInactive:false,selected:null,pending:null,canRetry:false,ack:false,reason:0,reasons:model.reasons,busy:false,error:'',notice:''},
  onLoad(q) {
    this.id=typeof q.id==='string'?q.id.toLowerCase():'';this.owner=actor(getApp().session.getState());this.epoch=0;this.offset=0;this.includeInactive=false;
    this.reader=readScope(this,getApp(),s=>model.allowed(s)&&actor(s)===this.owner,()=>{this.epoch++;return{items:[],total:0,selected:null,pending:null,canRetry:false,ack:false,notice:''};},()=>{if(this.writer)this.writer.dropPayload();});
  },
  onShow(){this.visible=true;this.reader.show();return this.load();},onHide(){this.visible=false;this.reader.hide();},onUnload(){this.onHide();this.disposed=true;this.reader.dispose();},
  alive(){return this.visible&&!this.disposed&&actor(getApp().session.getState())===this.owner&&model.allowed(getApp().session.getState());},
  load(){if(this.working)return;return this.reader.run(async()=>{
    if(this.id&&!isGuid(this.id))throw Object.assign(new Error('INVALID_PATH'),{code:'INVALID_PATH'});
    const session=getApp().session;const page=model.page(await session.request(`/api/app/education-school/my-invitations?IncludeInactive=${this.includeInactive}&SkipCount=${this.offset}&MaxResultCount=20`),session.getState());
    const selected=this.id?model.invitation(await session.request(`/api/app/education-school/my-invitation/${this.id}`),session.getState(),this.id):null;
    return{...page,selected};
  },view=>{if(view.selected&&!this.writer)this.writer=command(wx,getApp().session,getApp().configuration,this.id,()=>this.alive());this.setData({...view,offset:this.offset,includeInactive:this.includeInactive,ack:false,notice:'',...(this.writer?this.writer.state():{})});});},
  select(e){if(this.data.busy||this.data.pending)return;const row=this.data.items.find(x=>x.id===e.currentTarget.dataset.id);if(!row)return;this.id=row.id;this.writer=null;return this.load();},
  toggle(e){if(this.data.busy||this.data.pending)return;this.includeInactive=!!e.detail.value;this.offset=0;this.id='';this.writer=null;return this.load();},
  acknowledge(e){if(!this.data.busy)this.setData({ack:e.detail.value.includes('confirmed')});},chooseReason(e){if(!this.data.busy)this.setData({reason:Number(e.detail.value)});},
  accept(){return this.respond(0);},decline(){return this.respond(1);},
  async respond(response){
    const row=this.data.selected;if(!this.alive()||this.data.busy||this.working||!row||!row.canRespond||this.data.pending||response===0&&!this.data.ack)return;
    const context=getApp().session.getState().context,ticket=this.epoch;
    if(!await ask(response===0?`确认这是本人的学校邀请：${row.school}（${row.schoolCode}），角色为${row.roleName}。加入不自动建立任教或在班关系。`:`向学校反馈“${model.reasons[this.data.reason]}”，并拒绝这份邀请？`))return;
    if(!this.alive()||context!==getApp().session.getState().context||ticket!==this.epoch||this.working||this.data.busy||this.data.pending||this.data.selected!==row||!row.canRespond)return;
    return this.write(()=>this.writer.start({response,concurrencyStamp:row.stamp,confirmIdentityAndSchool:response===0,declineReason:response===1?this.data.reason:null}));
  },
  async write(action){if(!this.alive()||this.working)return;this.working=true;const ticket=this.epoch,context=getApp().session.getState().context;this.setData({busy:true,error:'',notice:''});
    try{const row=await action();if(this.alive()&&ticket===this.epoch&&context===getApp().session.getState().context)this.setData({selected:row||this.data.selected,ack:false,notice:row?'已核对原操作结果。请回工作区刷新当前身份；加入不等于已分班或任教。':'未找到这个操作的成功回执，请核对原状态；不会自动换成新操作。'});}
    catch(e){if(this.alive()&&ticket===this.epoch)this.setData({error:message(e)});}
    finally{this.working=false;if(this.alive()&&ticket===this.epoch&&context===getApp().session.getState().context)this.setData({busy:false,...this.writer.state()});}
  },
  recover(){return this.write(()=>this.writer.recover());},retry(){return this.write(()=>this.writer.retry());},
  async abandon(){if(!this.alive()||this.data.busy||!this.data.pending)return;const ticket=this.epoch,operation=this.data.pending.operationId;if(!await ask('移除本机待确认标识不会撤销可能已成功的加入或拒绝。请先查询原回执，并回学校核对当前状态。'))return;if(!this.alive()||this.data.busy||ticket!==this.epoch||!this.data.pending||operation!==this.data.pending.operationId)return;this.writer.abandon();return this.load();},
  paginate(e){const n=Number(e.currentTarget.dataset.direction);if(this.data.busy||this.data.pending||![-1,1].includes(n)||this.offset+n*20<0||n===1&&this.offset+20>=this.data.total)return;this.offset+=n*20;this.id='';this.writer=null;return this.load();},
  workspace(){if(!this.data.busy)wx.reLaunch({url:'/pages/workspace/index'});},
});
