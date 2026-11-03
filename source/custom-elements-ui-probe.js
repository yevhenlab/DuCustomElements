(() => {
 const report=x=>CPPMod.sendModAction('CustomElements',95,[0,0],JSON.stringify(x));
 const style=n=>{if(!n)return null;const s=getComputedStyle(n);return {background:s.backgroundColor,font:s.fontFamily,fontSize:s.fontSize,height:s.height};};
 report({version:window.customElementsPreview.version,
  custom:style(document.querySelector('.ce-contents .drilldown_root > .drilldownElementBody')),
  native:style(document.querySelector('.talent_fields .drilldown_root > .drilldownElementBody')),
  caption:style(document.querySelector('.ce-contents .item_description_holder p')),
  sections:Array.from(document.querySelectorAll('.ce-contents .drilldown_root > .drilldownElementBody')).map(n=>{
   const label=n.querySelector('.label'),value=n.querySelector('.string');
   return {label:label?label.textContent:null,count:value?value.textContent:null,
    valueClass:value?value.className:null,html:n.outerHTML.slice(0,950)};
  })});
})();
