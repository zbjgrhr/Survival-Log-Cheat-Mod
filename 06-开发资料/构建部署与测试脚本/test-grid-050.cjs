const {chromium} = require('C:/Users/zbjgr/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const fs = require('fs');
const path = require('path');
const assert = require('assert');
(async () => {
 const browser = await chromium.launch({channel:'msedge', headless:true});
 const page = await browser.newPage({viewport:{width:1280,height:720}});
 const errors=[]; page.on('pageerror', e=>errors.push(e.message));
 const root='C:/tool/steam/steamapps/common/Survival Log/SurvivalLog_Data/StreamingAssets/WebUI';
 await page.route('http://trainer.test/**', async route=>{
   const rel=decodeURIComponent(new URL(route.request().url()).pathname).replace(/^\//,'');
   const target=rel==='webui-bag.js'?path.join(__dirname,'webui-050/webui-bag.js'):rel==='UI/ToolTable/ToolTable.html'?path.join(__dirname,'webui-050/ToolTable.html'):rel==='UI/BackpackUI/BackpackUI.html'?path.join(__dirname,'webui-050/BackpackUI.html'):path.join(root,rel);
   if (!fs.existsSync(target)) return route.fulfill({status:404,body:''});
   await route.fulfill({body:fs.readFileSync(target),contentType:target.endsWith('.js')?'application/javascript':target.endsWith('.html')?'text/html':'application/octet-stream'});
 });
 await page.goto('http://trainer.test/UI/BrewPanel/BrewPanel.html');
 await page.evaluate(()=>window.postMessage({type:'WebUI_Brew_BagMsg',data:{bagCols:30,bagRows:30,barrelCols:30,barrelRows:30,bagItems:[],barrelItems:[]}},'*'));
 await page.waitForSelector('.trainer-grid-scroll');
 const geometry=await page.evaluate(()=>{
   const grid=document.getElementById('bagGrid'), viewport=grid.parentElement;
   const stale=grid.getBoundingClientRect();
   viewport.scrollLeft=10000; viewport.scrollTop=10000;
   const r=grid.getBoundingClientRect(),cfg=bagBag.getConfig();
   const x=r.left+cfg.padding+29*(cfg.slotSize+cfg.gap)+cfg.slotSize/2;
   const y=r.top+cfg.padding+29*(cfg.slotSize+cfg.gap)+cfg.slotSize/2;
   const panel=document.querySelector('.brew-system').getBoundingClientRect();
   return {cell:bagBag.getCursorCell(x,y,stale),drop:bagBag.getGridCoordinates(x,y,1,1,stale),
     inside:bagBag.isMouseOver(x,y,stale),outside:bagBag.isMouseOver(r.left+1,r.top+1,stale),
     scrollX:viewport.scrollLeft,scrollY:viewport.scrollTop,
     slots:grid.querySelectorAll('.slot').length,
     bounds:{x:panel.x,y:panel.y,right:panel.right,bottom:panel.bottom}};
 });
 assert.deepEqual(geometry.cell,{row:29,col:29});
 assert.deepEqual(geometry.drop,{row:29,col:29});
 assert(geometry.inside && !geometry.outside);
 assert(geometry.scrollX>0 && geometry.scrollY>0 && geometry.slots===900);
 assert(geometry.bounds.x>=0 && geometry.bounds.y>=0 && geometry.bounds.right<=1280 && geometry.bounds.bottom<=720, JSON.stringify(geometry.bounds));
 assert.deepEqual(errors,[]);
 await page.screenshot({path:path.join(__dirname,'grid-050-brew.png')});
 console.log(JSON.stringify({ok:true,geometry,errors}));
 await page.goto('http://trainer.test/UI/BackpackUI/BackpackUI.html');
 await page.evaluate(()=>{
   window.postMessage({type:'WebUI_Backpack_InfoAMsg',data:{isDualBag:true,gridCols:30,gridRows:30,ownerId:1,backpackName:'背包'}},'*');
   window.postMessage({type:'WebUI_Backpack_InfoBMsg',data:{gridCols:30,gridRows:30,ownerId:2,backpackName:'储物柜'}},'*');
 });
 await page.waitForSelector('.grid-wrapper');
 const backpack=await page.evaluate(()=>{
   return [...document.querySelectorAll('.grid-wrapper')].map(el=>{
    el.scrollTop=100000;el.scrollLeft=100000;const r=el.getBoundingClientRect();
    return {x:r.x,y:r.y,right:r.right,bottom:r.bottom,sx:el.scrollLeft,sy:el.scrollTop};
   });
 });
 assert.equal(backpack.length,2);assert(backpack.every(p=>p.x>=0&&p.y>=0&&p.right<=1280&&p.bottom<=720&&p.sx>0&&p.sy>0),JSON.stringify(backpack));
 await page.screenshot({path:path.join(__dirname,'grid-050-backpack.png')});
 console.log('Backpack and storage scroll:',JSON.stringify(backpack));
 await page.evaluate(()=>{
   window.__drops=[];
   window.addEventListener('message',e=>{if(e.data&&e.data.type==='DRAG_ITEM')window.__drops.push(e.data.data);});
   window.postMessage({type:'WebUI_Backpack_ItemsBMsg',data:{itemsJson:JSON.stringify([{id:999,x:29,y:29,w:1,h:1,name:'离线测试物品',icon:'',count:1}])}},'*');
 });
 const item=page.locator('.grid-wrapper .item').first();await item.waitFor();
 const source=await item.boundingBox();
 const target=await page.evaluate(()=>{
   const r=document.querySelector('.grid-container').getBoundingClientRect();
   return {x:r.left+r.width*29.5/30,y:r.top+r.height*29.5/30};
 });
 await page.mouse.move(source.x+source.width/2,source.y+source.height/2);
 await page.mouse.down();await page.mouse.move(target.x,target.y,{steps:12});await page.mouse.up();
 const drop=await page.evaluate(()=>window.__drops);
 assert(drop.length===1&&drop[0].dstOwnerId===1&&drop[0].x===29&&drop[0].y===29,JSON.stringify(drop));
 console.log('PASS: scrolled storage to backpack drag targets cell 29,29, not ground.');
 const reports=[];
 await page.addInitScript(()=>{
   window.__testBags=[];
   let factory;
   Object.defineProperty(window,'createGridBag',{configurable:true,get:()=>factory,set:f=>{
     factory=function(...args){const bag=f(...args);window.__testBags.push(bag);return bag;};
   }});
 });
 for (const size of [{width:1280,height:720},{width:1024,height:768}]) {
  await page.setViewportSize(size);
  for (const folder of fs.readdirSync(path.join(root,'UI'))) {
   if(process.env.GRID_TEST_PAGE && folder!==process.env.GRID_TEST_PAGE) continue;
   const dir=path.join(root,'UI',folder);
   if(!fs.statSync(dir).isDirectory()) continue;
   for(const name of fs.readdirSync(dir).filter(n=>n.endsWith('.html'))) {
    if(!fs.readFileSync(path.join(dir,name),'utf8').includes('createGridBag(')) continue;
    console.log('Checking',folder,size.width);
    errors.length=0;
    await page.goto('http://trainer.test/UI/'+folder+'/'+name,{waitUntil:'domcontentloaded',timeout:10000});
    const checks=await page.evaluate(()=>{
      for(const bag of window.__testBags) {bag.resize(30,30);bag.refresh([]);}
      window.dispatchEvent(new Event('resize'));
      return window.__testBags.map(bag=>{
        const el=document.getElementById(bag.getContainerId());
        const p=el.parentElement;const r=p.getBoundingClientRect();
        return {id:el.id,scroll:p.classList.contains('trainer-grid-scroll'),right:r.right,bottom:r.bottom,left:r.left,top:r.top,
          ancestors:Array.from((function*(n){while(n&&n!==document.body){yield n;n=n.parentElement;}})(p)).map(n=>({name:n.className,w:n.offsetWidth,x:n.getBoundingClientRect().x,position:getComputedStyle(n).position,transform:getComputedStyle(n).transform}))};
      });
    });
    reports.push({page:folder,size,checks,errors:[...errors]});
   }
  }
 }
 fs.writeFileSync(path.join(__dirname,'grid-050-all.json'),JSON.stringify(reports,null,2));
 const failed=reports.filter(r=>r.errors.length||r.checks.some(c=>!c.scroll||c.left<0||c.top<0||c.right>r.size.width+1||c.bottom>r.size.height+1));
 console.log(JSON.stringify({pages:reports.length,failures:failed}));
 assert.equal(failed.length,0,'Grid viewport coverage');
 await browser.close();
})().catch(e=>{console.error(e);process.exit(1)});
