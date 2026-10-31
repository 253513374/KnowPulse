const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm'),crypto=require('node:crypto');
const {createRequire}=require('node:module'),model=require('../utils/inbox');
const [school,owner,notice,reference,publication,feedbackId,feedbackVersion,release,other]=Array.from({length:9},()=>crypto.randomUUID());
const at='2026-10-18T00:00:00Z';
function fixture() {
  let state={authenticated:true,generation:1,context:{accountUserId:owner,tenantId:null,grantedPermissions:['KnowPulse.Education.AssessmentParticipate','KnowPulse.Education.FeedbackView','KnowPulse.Education.TeachingPlanView'],workspaces:[{role:2,schoolId:school,classes:[{}]}]}};
  const f={requests:[],links:[],rows:{0:[{id:notice,schoolId:school,source:0,kind:1,referenceId:reference,publicationId:publication,resultReleaseId:null,availableAtUtc:at,readAtUtc:null,canOpen:true,weChatDelivery:'NotConfigured',rawAnswer:'PRIVATE-ANSWER'}],
    1:[{id:feedbackVersion,schoolId:school,source:1,kind:3,referenceId:feedbackId,publicationId:feedbackVersion,resultReleaseId:null,availableAtUtc:at,readAtUtc:null,canOpen:true,weChatDelivery:'NotConfigured',text:'PRIVATE-FEEDBACK'}],
    2:[{id:crypto.randomUUID(),schoolId:school,source:2,kind:8,referenceId:reference,publicationId:publication,stepId:other,resultReleaseId:null,availableAtUtc:at,readAtUtc:null,canOpen:true,weChatDelivery:'NotConfigured',privateNote:'PRIVATE-TEACHER-NOTE'}]}};
  const listeners=new Set(),platform={navigateTo(o){f.links.push(o.url);},reLaunch(){}};
  const session={getState:()=>state,refreshWorkspace:async()=>state,async request(url,method='GET',body){f.requests.push({url,method,body:structuredClone(body)});
    if(url.includes('/sources?'))return [0,1,2];
    if(url.includes('/my-list?')){if(f.readWait)return f.readWait;const q=new URL('https://api.invalid'+url).searchParams;return {totalCount:f.rows[q.get('Source')].length,items:structuredClone(f.rows[q.get('Source')])};}
    if(url.endsWith('/read-my')){const row=f.rows[body.source].find((r)=>r.id===body.messageId);row.readAtUtc=at;return structuredClone(row);}
    throw new Error('Unexpected route '+url);
  }};
  const app={session,subscribe(fn){listeners.add(fn);return()=>listeners.delete(fn);}};const file=path.resolve(__dirname,'../pages/inbox/index.js');let definition;
  new vm.Script(fs.readFileSync(file,'utf8')).runInNewContext({require:createRequire(file),Page:(d)=>definition=d,wx:platform,getApp:()=>app});
  const page={data:structuredClone(definition.data),setData(value){assert.ok(Buffer.byteLength(JSON.stringify(value))<1024*1024);Object.assign(this.data,value);}};
  for(const [key,value]of Object.entries(definition))if(typeof value==='function')page[key]=value.bind(page);page.onLoad({schoolId:school});
  return Object.assign(f,{page,session,change(next){state=next;listeners.forEach((fn)=>fn(next));}});
}
const click=(id)=>({currentTarget:{dataset:{id}}});
test('inbox source selection replaces the page without caching source bodies or inventing cross-source totals',async()=>{
  const f=fixture();await f.page.onShow();assert.equal(f.page.data.sources.length,3);assert.equal(f.page.data.total,1);assert.ok(!JSON.stringify(f.page.data).includes('PRIVATE-ANSWER'));
  await f.page.choose({detail:{value:1}});assert.equal(f.page.data.items[0].source,1);assert.equal(f.page.data.total,1);assert.ok(!JSON.stringify(f.page.data).includes('PRIVATE-FEEDBACK'));
  assert.throws(()=>model.message(f.rows[1][0],other,1),{code:'INVALID_RESPONSE'});assert.throws(()=>model.message(f.rows[1][0],school,0),{code:'INVALID_RESPONSE'});
});
test('message reading calls only the common receipt and pins feedback or result navigation without starting an exam',async()=>{
  const f=fixture();await f.page.onShow();await f.page.choose({detail:{value:1}});await f.page.open(click(feedbackVersion));
  assert.equal(f.requests.filter((r)=>r.method==='POST').length,1);assert.ok(f.links[0].endsWith('&versionId='+feedbackVersion));assert.ok(!f.requests.some((r)=>r.url.includes('/open-my')||r.url.includes('start-or-resume')));
  f.rows[0][0].kind=2;f.rows[0][0].resultReleaseId=release;await f.page.choose({detail:{value:0}});await f.page.open(click(notice));assert.ok(f.links[1].endsWith('&releaseId='+release));assert.ok(f.links[1].includes('taskRevisionId='+publication));
});
test('withdrawn feedback is marked message-read without navigating to content or another publication',async()=>{
  const f=fixture();f.rows[1][0].kind=4;f.rows[1][0].canOpen=false;await f.page.onShow();await f.page.choose({detail:{value:1}});await f.page.open(click(feedbackVersion));
  assert.equal(f.links.length,0);assert.equal(f.page.data.items[0].isRead,true);assert.ok(f.page.data.notice.includes('撤回'));
});
test('hidden-page or changed-account responses cannot restore a prior inbox projection',async()=>{
  const f=fixture();let resolve;f.readWait=new Promise((r)=>resolve=r);const loading=f.page.onShow();await new Promise(setImmediate);f.page.onHide();resolve({totalCount:1,items:f.rows[0]});await loading;assert.equal(f.page.data.items.length,0);
  f.readWait=null;await f.page.onShow();const state=f.session.getState();f.change({...state,generation:2,context:{...state.context,accountUserId:other}});assert.equal(f.page.data.items.length,0);await f.page.open(click(notice));assert.equal(f.requests.filter((r)=>r.method==='POST').length,0);
});
test('plan notices pin school, publication and public step without exposing private handling metadata',async()=>{
  const f=fixture();await f.page.onShow();await f.page.choose({detail:{value:2}});const row=f.page.data.items[0];
  assert.ok(!JSON.stringify(f.page.data).includes('PRIVATE-TEACHER-NOTE'));assert.equal(row.stepId,other);await f.page.open(click(row.id));
  assert.equal(f.links[0],`/pages/teaching-plan/index?schoolId=${school}&id=${reference}&versionId=${publication}&stepId=${other}`);
  assert.ok(!f.requests.some((r)=>r.url.includes('my-activities')||r.url.includes('open-my')));
  f.rows[2][0].kind=6;f.rows[2][0].stepId=null;f.rows[2][0].canOpen=false;await f.page.load();await f.page.open(click(row.id));assert.equal(f.links.length,1);assert.ok(f.page.data.items[0].isRead);
  assert.throws(()=>model.message({...f.rows[2][0],canOpen:true},school,2),{code:'INVALID_RESPONSE'});
  assert.throws(()=>model.message({...f.rows[2][0],kind:8,stepId:null},school,2),{code:'INVALID_RESPONSE'});
  const state=f.session.getState();assert.equal(model.allowed({...state,context:{...state.context,grantedPermissions:[]}},school,2),false);
});
