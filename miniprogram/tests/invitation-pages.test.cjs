const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm'),crypto=require('node:crypto');
const {createRequire}=require('node:module'),model=require('../utils/invitations'),command=require('../utils/invitation-command');
const [owner,school,id,member,other]=Array.from({length:5},()=>crypto.randomUUID());
const config={apiBaseUrl:'https://api.example.invalid',appKey:'main',clientId:'KnowPulse_WeChat_main'};
function fixture(){
  let state={authenticated:true,generation:1,context:{accountUserId:owner,tenantId:null,grantedPermissions:[],workspaces:[]}};
  const f={posts:[],gets:[],links:[],store:new Map(),confirm:true,row:{id,schoolId:school,schoolName:'学校甲',schoolCode:'SCHOOL-A',targetUserId:owner,role:1,effectiveFromUtc:'2026-10-18T00:00:00Z',effectiveToUtc:null,
    expiresAtUtc:'2026-10-20T00:00:00Z',recordedAtUtc:'2026-10-18T00:00:00Z',status:0,completedAtUtc:null,membershipId:null,responseOperationId:null,declineReason:null,canRespond:true,concurrencyStamp:'v1',revocationReason:'PRIVATE-ADMIN-REASON'}};
  const listeners=new Set();f.platform={getStorageSync:k=>f.store.get(k),setStorageSync:(k,v)=>f.store.set(k,structuredClone(v)),removeStorageSync:k=>f.store.delete(k),
    getRandomValues(o){const b=crypto.randomBytes(16);o.success({randomValues:b.buffer.slice(b.byteOffset,b.byteOffset+b.length)});},showModal(o){o.success({confirm:f.confirm});},reLaunch(o){f.links.push(o.url);}};
  f.session={getState:()=>state,refreshWorkspace:async()=>state,async request(url,method='GET',body){
    if(method==='POST'){assert.equal(url,`/api/app/education-school/respond-to-invitation/${id}`);f.posts.push(structuredClone(body));
      f.row={...f.row,status:body.response===0?1:2,membershipId:body.response===0?member:null,responseOperationId:body.operationId,declineReason:body.declineReason,completedAtUtc:'2026-10-18T01:00:00Z',canRespond:false,concurrencyStamp:'v2'};
      if(f.lose){f.lose=false;throw Object.assign(new Error('network'),{code:'NETWORK_UNCONFIRMED'});}return structuredClone(f.row);
    }
    f.gets.push(url);if(url.includes('/my-invitations?')){if(f.wait)return f.wait;return{totalCount:1,items:[structuredClone(f.row)]};}
    assert.equal(url,`/api/app/education-school/my-invitation/${id}`);return structuredClone(f.row);
  }};
  const app={session:f.session,configuration:config,subscribe(fn){listeners.add(fn);return()=>listeners.delete(fn);}};
  f.change=next=>{state=next;listeners.forEach(fn=>fn(next));};f.app=app;
  return f;
}
function page(f){const file=path.resolve(__dirname,'../pages/invitations/index.js');let def;new vm.Script(fs.readFileSync(file,'utf8')).runInNewContext({require:createRequire(file),Page:d=>def=d,wx:f.platform,getApp:()=>f.app});
  const p={data:structuredClone(def.data),setData(v){Object.assign(this.data,v);}};for(const[k,v]of Object.entries(def))if(typeof v==='function')p[k]=v.bind(p);p.onLoad({});return p;}
const click={currentTarget:{dataset:{id}}};
test('invitations are account-bound metadata and require no existing school or functional permission',()=>{
  const f=fixture(),s=f.session.getState();assert.equal(model.allowed(s),true);assert.ok(!JSON.stringify(model.invitation(f.row,s,id)).includes('PRIVATE-ADMIN'));
  assert.throws(()=>model.invitation({...f.row,targetUserId:other},s,id),{code:'INVALID_RESPONSE'});
  assert.throws(()=>model.invitation({...f.row,status:3,canRespond:true},s,id),{code:'INVALID_RESPONSE'});
});
test('lost response recovery after cold start uses the original nonce and does not repeat membership creation',async()=>{
  const f=fixture();f.lose=true;let writer=command(f.platform,f.session,config,id,()=>true);
  await assert.rejects(writer.start({response:0,concurrencyStamp:'v1',confirmIdentityAndSchool:true,declineReason:null}),{code:'NETWORK_UNCONFIRMED'});
  const ref=[...f.store.values()][0];assert.deepEqual(Object.keys(ref).sort(),['invitationId','operationId','response'].sort());assert.equal(f.posts.length,1);
  writer=command(f.platform,f.session,config,id,()=>true);assert.equal(writer.state().canRetry,false);const row=await writer.recover();assert.equal(row.status,1);assert.equal(row.responseOperationId,ref.operationId);assert.equal(f.store.size,0);assert.equal(f.posts.length,1);
});
test('opening or cancelling the confirmation never joins and explicit confirmation posts only once',async()=>{
  const f=fixture(),p=page(f);await p.onShow();assert.equal(f.posts.length,0);await p.select(click);assert.equal(p.data.selected.status,0);assert.ok(!JSON.stringify(p.data).includes('PRIVATE-ADMIN'));
  await p.accept();assert.equal(f.posts.length,0);p.acknowledge({detail:{value:['confirmed']}});f.confirm=false;await p.accept();assert.equal(f.posts.length,0);
  f.confirm=true;await p.accept();assert.equal(f.posts.length,1);assert.equal(p.data.selected.status,1);await p.accept();assert.equal(f.posts.length,1);assert.equal(f.store.size,0);
});
test('hidden-page and switched-account results cannot repopulate the old invitation view or replay a response',async()=>{
  const f=fixture(),p=page(f);let resolve;f.wait=new Promise(r=>resolve=r);const load=p.onShow();await new Promise(setImmediate);p.onHide();resolve({totalCount:1,items:[f.row]});await load;assert.equal(p.data.items.length,0);
  f.wait=null;await p.onShow();await p.select(click);const s=f.session.getState();f.change({...s,generation:2,context:{...s.context,accountUserId:other}});assert.equal(p.data.selected,null);await p.accept();assert.equal(f.posts.length,0);
});
